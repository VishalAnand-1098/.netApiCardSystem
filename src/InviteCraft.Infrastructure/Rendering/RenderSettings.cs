namespace InviteCraft.Infrastructure.Rendering;

/// <summary>Configuration section "Rendering": the headless browser that draws downloadable cards.</summary>
public sealed class RenderSettings
{
    public const string SectionName = "Rendering";

    /// <summary>false → the background workers do not start (no queue processing, no start-up database access). Tests turn this off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Playwright browser channel (e.g. "chrome", "msedge"). Ignored when <see cref="BrowserPath"/> is set. Empty = Playwright's own Chromium (<c>playwright install chromium</c>).</summary>
    public string BrowserChannel { get; set; } = "chrome";

    /// <summary>Explicit path to a Chrome/Chromium executable (e.g. the one in the Docker image).</summary>
    public string? BrowserPath { get; set; }

    /// <summary>Required when Chromium runs as root inside a container.</summary>
    public bool NoSandbox { get; set; }

    /// <summary>Cards drawn at the same time (each uses its own browser context). Also the number of background workers.</summary>
    public int MaxConcurrency { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>Crop-mark margin (mm) added around the bleed on Print PDFs.</summary>
    public decimal PrintMarginMm { get; set; } = 8;
}
