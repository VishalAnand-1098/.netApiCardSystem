using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace InviteCraft.Infrastructure.Rendering;

/// <summary>
/// Draws cards with headless Chromium. The page loads the same renderer the editor preview uses
/// (<c>InviteCraftRender.mount</c>), so a download is the preview at a higher pixel density; JPG/PNG are screenshots at
/// <c>deviceScaleFactor = dpi / 96</c> and PDFs are printed at the card's physical size (vector text).
///
/// Isolation: every render gets its own browser context, JavaScript can only reach the renderer page, and every network
/// request is intercepted — the page can read <c>/media/**</c> (fonts and artwork, answered straight from storage) and
/// nothing else, so customer text can never make the browser fetch an external URL.
/// </summary>
internal sealed class PlaywrightCardRenderer(
    IFileStorage storage,
    IOptions<RenderSettings> options,
    ILogger<PlaywrightCardRenderer> logger) : ICardRenderer, IAsyncDisposable
{
    private const string Origin = "http://render.local";
    private const int JpegQuality = 92;

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<string> HostPage = new(() => ReadResource("render.html"));
    private static readonly Lazy<string> RendererScript = new(() => ReadResource("renderer.js"));

    private readonly RenderSettings settings = options.Value;
    private readonly SemaphoreSlim concurrency = new(Math.Max(1, options.Value.MaxConcurrency));
    private readonly SemaphoreSlim browserLock = new(1, 1);
    private readonly string mediaPrefix = ResolveMediaPrefix(storage);
    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task<RenderedFile> RenderAsync(CardRenderRequest request, CancellationToken cancellationToken = default)
    {
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds + 15));
            return await RenderCoreAsync(request, timeout.Token);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<RenderedFile> RenderCoreAsync(CardRenderRequest request, CancellationToken cancellationToken)
    {
        var template = request.Template;
        var isPdf = request.Format is DownloadFormat.Pdf or DownloadFormat.PrintPdf;
        var scale = isPdf ? 1f : request.Dpi / 96f;
        // Previews are drawn from the web-resolution artwork; everything sharper uses the private originals.
        var useOriginals = isPdf || request.Dpi > DownloadPolicy.PreviewDpi;

        var cardWidthPx = (double)template.WidthMm / 25.4 * 96;
        var cardHeightPx = (double)template.HeightMm / 25.4 * 96;

        var payload = JsonSerializer.Serialize(new
        {
            template,
            language = request.Language,
            pageIndex = isPdf ? -1 : request.PageIndex,
            values = request.Values,
            watermark = request.Watermark is { } w ? new { text = w.Text, opacity = w.Opacity } : null,
            print = request.Format == DownloadFormat.PrintPdf ? new { bleedMm = template.BleedMm, marginMm = settings.PrintMarginMm } : null,
        }, PayloadJson);

        var activeBrowser = await GetBrowserAsync(cancellationToken);
        await using var context = await activeBrowser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = (int)Math.Ceiling(cardWidthPx), Height = (int)Math.Ceiling(cardHeightPx) },
            DeviceScaleFactor = scale,
            ServiceWorkers = ServiceWorkerPolicy.Block,
            AcceptDownloads = false,
            Locale = "en-US",
            ColorScheme = ColorScheme.Light,
        });
        context.SetDefaultTimeout(settings.TimeoutSeconds * 1000);

        await context.RouteAsync("**/*", route => HandleRequestAsync(route, useOriginals));

        var page = await context.NewPageAsync();
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
                logger.LogWarning("Renderer page {Type}: {Text}", message.Type, message.Text);
        };
        page.PageError += (_, error) => logger.LogWarning("Renderer page error: {Error}", error);

        await page.GotoAsync($"{Origin}/render.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var mountJson = await page.EvaluateAsync<string>("json => InviteCraftRender.mount(JSON.parse(json)).then(result => JSON.stringify(result))", payload);
        using var mount = JsonDocument.Parse(mountJson);
        var fontsRequested = mount.RootElement.GetProperty("fontsRequested").GetInt32();
        var fontsLoaded = mount.RootElement.GetProperty("fontsLoaded").GetInt32();
        if (fontsLoaded < fontsRequested)
            logger.LogWarning("Only {Loaded} of {Requested} fonts loaded for template {Template}", fontsLoaded, fontsRequested, template.Code);

        var sheetWidthPx = mount.RootElement.GetProperty("widthPx").GetDouble();
        var sheetHeightPx = mount.RootElement.GetProperty("heightPx").GetDouble();

        switch (request.Format)
        {
            case DownloadFormat.Jpg or DownloadFormat.Png:
            {
                var jpeg = request.Format == DownloadFormat.Jpg;
                // Whole CSS pixels only: Chrome snaps the capture area, and this keeps the file size predictable (see DownloadPolicy.PixelsFor).
                var captureWidth = Math.Floor(sheetWidthPx);
                var captureHeight = Math.Floor(sheetHeightPx);
                var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Type = jpeg ? ScreenshotType.Jpeg : ScreenshotType.Png,
                    Quality = jpeg ? JpegQuality : null,
                    Clip = new Clip { X = 0, Y = 0, Width = (float)captureWidth, Height = (float)captureHeight },
                    Animations = ScreenshotAnimations.Disabled,
                    Scale = ScreenshotScale.Device,
                });
                return new RenderedFile(bytes, jpeg ? "image/jpeg" : "image/png", jpeg ? "jpg" : "png",
                    (int)Math.Round(captureWidth * scale), (int)Math.Round(captureHeight * scale));
            }

            default:
            {
                var pdf = await page.PdfAsync(new PagePdfOptions
                {
                    PreferCSSPageSize = true,
                    PrintBackground = true,
                    Margin = new Margin { Top = "0", Right = "0", Bottom = "0", Left = "0" },
                });
                return new RenderedFile(pdf, "application/pdf", "pdf", (int)Math.Round(sheetWidthPx), (int)Math.Round(sheetHeightPx));
            }
        }
    }

    // ------------------------------------------------------------------ request interception

    private async Task HandleRequestAsync(IRoute route, bool useOriginals)
    {
        try
        {
            var uri = new Uri(route.Request.Url);
            if (!string.Equals(uri.Host, "render.local", StringComparison.OrdinalIgnoreCase))
            {
                await route.AbortAsync("blockedbyclient");
                return;
            }

            var path = uri.AbsolutePath;
            if (path == "/render.html")
                await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", Body = HostPage.Value });
            else if (path == "/renderer.js")
                await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/javascript; charset=utf-8", Body = RendererScript.Value });
            else if (path.StartsWith(mediaPrefix, StringComparison.Ordinal))
                await FulfillFromStorageAsync(route, path[mediaPrefix.Length..], useOriginals);
            else
                await route.FulfillAsync(new RouteFulfillOptions { Status = 404 });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Renderer request failed: {Url}", route.Request.Url);
            try { await route.AbortAsync(); } catch { /* the page is already gone */ }
        }
    }

    private async Task FulfillFromStorageAsync(IRoute route, string relativePath, bool useOriginals)
    {
        var key = IFileStorage.PublicPrefix + Uri.UnescapeDataString(relativePath);
        if (key.Contains("..", StringComparison.Ordinal))
        {
            await route.FulfillAsync(new RouteFulfillOptions { Status = 400 });
            return;
        }

        // Artwork: swap the 2000 px web variant for the private original when the output is sharper than the web copy.
        if (useOriginals && key.EndsWith("/web.webp", StringComparison.Ordinal) &&
            (key.StartsWith("public/templates/", StringComparison.Ordinal) || key.StartsWith("public/invitations/", StringComparison.Ordinal)))
        {
            var folder = IFileStorage.PrivatePrefix + key[IFileStorage.PublicPrefix.Length..^"web.webp".Length];
            foreach (var extension in new[] { "png", "jpg", "webp" })
            {
                var original = await storage.OpenReadAsync($"{folder}original.{extension}");
                if (original is null)
                    continue;

                await using (original)
                    await FulfillStreamAsync(route, original, MimeFor(extension));
                return;
            }
        }

        var stream = await storage.OpenReadAsync(key);
        if (stream is null)
        {
            logger.LogWarning("Renderer asked for a missing file: {Key}", key);
            await route.FulfillAsync(new RouteFulfillOptions { Status = 404 });
            return;
        }

        await using (stream)
            await FulfillStreamAsync(route, stream, MimeFor(Path.GetExtension(key).TrimStart('.')));
    }

    private static async Task FulfillStreamAsync(IRoute route, Stream stream, string contentType)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = contentType, BodyBytes = buffer.ToArray() });
    }

    private static string MimeFor(string extension) => extension.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "woff2" => "font/woff2",
        "woff" => "font/woff",
        "ttf" => "font/ttf",
        "otf" => "font/otf",
        _ => "application/octet-stream",
    };

    // ------------------------------------------------------------------ browser lifetime

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (browser is { IsConnected: true })
            return browser;

        await browserLock.WaitAsync(cancellationToken);
        try
        {
            if (browser is { IsConnected: true })
                return browser;

            if (browser is not null)
                logger.LogWarning("The rendering browser disconnected; starting a new one.");

            playwright ??= await Playwright.CreateAsync();
            var launch = new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--force-color-profile=srgb", "--font-render-hinting=none", "--disable-dev-shm-usage",
                        .. settings.NoSandbox ? new[] { "--no-sandbox" } : []],
            };
            if (!string.IsNullOrWhiteSpace(settings.BrowserPath))
                launch.ExecutablePath = settings.BrowserPath;
            else if (!string.IsNullOrWhiteSpace(settings.BrowserChannel))
                launch.Channel = settings.BrowserChannel;

            browser = await playwright.Chromium.LaunchAsync(launch);
            logger.LogInformation("Rendering browser started: Chromium {Version}", browser.Version);
            return browser;
        }
        finally
        {
            browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (browser is not null)
                await browser.CloseAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing the rendering browser");
        }

        playwright?.Dispose();
        concurrency.Dispose();
        browserLock.Dispose();
    }

    private static string ResolveMediaPrefix(IFileStorage storage)
    {
        var sample = storage.GetPublicUrl(IFileStorage.PublicPrefix + "x") ?? "/media/x";
        return sample[..^1]; // "/media/"
    }

    private static string ReadResource(string name)
    {
        var assembly = typeof(PlaywrightCardRenderer).Assembly;
        var resource = $"InviteCraft.Infrastructure.Rendering.Assets.{name}";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' not found. Run `npm run build:renderer` in frontend/.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
