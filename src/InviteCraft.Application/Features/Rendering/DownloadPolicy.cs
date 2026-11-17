using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Rendering;

/// <summary>What one invitation may export right now.</summary>
public sealed record DownloadEntitlement(
    string PackageCode,
    string PackageName,
    IReadOnlyList<DownloadFormat> Formats,
    int MaxDpi,
    int? MaxDownloads,
    bool Watermark,
    bool PreviewOnly);

/// <summary>
/// Decides what an invitation may download. A file leaves the server only after the invitation is paid for.
/// <list type="bullet">
/// <item>Unlocked (purchased) → the purchased package's formats, resolution, allowance and watermark setting.</item>
/// <item>Not purchased, any template tier → no file. The on-screen preview stays; nothing can be downloaded.</item>
/// </list>
/// </summary>
public static class DownloadPolicy
{
    public const string PreviewCode = "PREVIEW";
    public const int PreviewDpi = 96;
    /// <summary>Pixel densities offered for JPG/PNG (up to the package's limit).</summary>
    public static readonly int[] DpiSteps = [96, 150, 300];
    /// <summary>Density recorded for PDF downloads (vector output: the number only documents the embedded image quality).</summary>
    public const int PdfDpi = 300;
    public const int MaxInProgressPerUser = 3;
    public const int FileRetentionDays = 7;

    public static DownloadEntitlement Resolve(Invitation invitation, Template template, IReadOnlyList<Package> packages)
    {
        if (invitation.IsUnlocked && invitation.UnlockedPackageId is { } unlockedId && packages.FirstOrDefault(p => p.Id == unlockedId) is { } purchased)
            return FromPackage(purchased, watermark: !purchased.RemovesWatermark);

        return Locked();
    }

    /// <summary>Unpaid invitation: the caller must not queue or stream a file.</summary>
    public static DownloadEntitlement Locked() =>
        new(PreviewCode, "Preview", [], PreviewDpi, MaxDownloads: 0, Watermark: true, PreviewOnly: true);

    private static DownloadEntitlement FromPackage(Package package, bool watermark) => new(
        package.Code, package.Name,
        package.AllowedFormats.Select(f => Enum.TryParse<DownloadFormat>(f, out var format) ? (DownloadFormat?)format : null)
            .Where(f => f.HasValue).Select(f => f!.Value).ToList(),
        package.MaxDpi, package.MaxDownloads, watermark, PreviewOnly: false);

    /// <summary>Densities a JPG/PNG may use, lowest first.</summary>
    public static IReadOnlyList<int> AllowedDpis(DownloadEntitlement entitlement) =>
        DpiSteps.Where(d => d <= entitlement.MaxDpi).ToList();

    public static bool IsImage(DownloadFormat format) => format is DownloadFormat.Jpg or DownloadFormat.Png;

    public static (string Mime, string Extension) FileType(DownloadFormat format) => format switch
    {
        DownloadFormat.Jpg => ("image/jpeg", "jpg"),
        DownloadFormat.Png => ("image/png", "png"),
        _ => ("application/pdf", "pdf"),
    };

    /// <summary>
    /// Pixel size of an export. The card is laid out in whole CSS pixels (96 per inch) and drawn at <c>dpi / 96</c> times that, so
    /// 127 mm at 300 dpi is exactly 1500 px, and 178 mm is 672 CSS px → 2100 px (the sub-pixel remainder is trimmed, never padded).
    /// </summary>
    public static int PixelsFor(decimal millimetres, int dpi) =>
        (int)Math.Round(Math.Floor(millimetres / 25.4m * 96m) * dpi / 96m, MidpointRounding.AwayFromZero);

    public static string QualityLabel(int dpi) => dpi switch
    {
        <= 96 => "Web",
        <= 150 => "HD",
        _ => "Print quality",
    };
}
