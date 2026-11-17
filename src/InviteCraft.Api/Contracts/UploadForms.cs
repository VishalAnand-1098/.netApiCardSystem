using InviteCraft.Domain.Enums;

namespace InviteCraft.Api.Contracts;

/// <summary>multipart/form-data: file, assetType (Thumbnail|Preview|Background|Decoration|Sample), pageIndex.</summary>
public sealed class UploadTemplateAssetForm
{
    public IFormFile? File { get; set; }
    public TemplateAssetType AssetType { get; set; } = TemplateAssetType.Background;
    public int PageIndex { get; set; }
}

/// <summary>multipart/form-data: woff2File (required), ttfFile (optional) + font metadata.</summary>
public sealed class UploadFontForm
{
    public IFormFile? Woff2File { get; set; }
    public IFormFile? TtfFile { get; set; }
    public string FamilyName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Weight { get; set; } = 400;
    public string Style { get; set; } = "normal";
    public List<string> Scripts { get; set; } = [];
    public string? License { get; set; }
}

/// <summary>multipart/form-data: file + the Image field it belongs to.</summary>
public sealed class UploadInvitationImageForm
{
    public IFormFile? File { get; set; }
    public string FieldKey { get; set; } = string.Empty;
}

public static class UploadLimits
{
    /// <summary>Request ceiling for upload endpoints (the services enforce tighter per-file limits).</summary>
    public const long MaxRequestBytes = 20 * 1024 * 1024;
}
