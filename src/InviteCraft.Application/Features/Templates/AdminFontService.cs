using FluentValidation;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Common.Validation;
using InviteCraft.Domain.Entities.Templates;

namespace InviteCraft.Application.Features.Templates;

public sealed record FontDto(
    Guid Id,
    string FamilyName,
    string DisplayName,
    int Weight,
    string Style,
    IReadOnlyList<string> Scripts,
    string? Url,
    bool HasTtf,
    string? License,
    bool IsActive,
    int TemplateCount,
    DateTimeOffset CreatedAt);

/// <summary>?search=&amp;script=devanagari&amp;isActive=&amp;page=&amp;pageSize=</summary>
public sealed record FontQuery : PagedRequest
{
    public string? Script { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record FontMetadata(
    string FamilyName,
    string DisplayName,
    int Weight,
    string Style,
    IReadOnlyList<string> Scripts,
    string? License);

public sealed record UpdateFontRequest(string DisplayName, IReadOnlyList<string> Scripts, string? License);

public static class FontScripts
{
    /// <summary>Writing systems the platform supports (Latin + major Indian scripts + Arabic for Urdu).</summary>
    public static readonly string[] Supported =
        ["latin", "devanagari", "bengali", "gujarati", "gurmukhi", "tamil", "telugu", "kannada", "malayalam", "odia", "arabic"];
}

internal sealed class FontMetadataValidator : AppValidator<FontMetadata>
{
    public FontMetadataValidator()
    {
        RuleFor(x => x.FamilyName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Weight).Must(w => w is >= 100 and <= 900 && w % 100 == 0).WithMessage("Weight must be 100–900 in steps of 100.");
        RuleFor(x => x.Style).Must(s => s is "normal" or "italic").WithMessage("Style must be normal or italic.");
        RuleFor(x => x.Scripts).NotEmpty().WithMessage("Select at least one script.")
            .Must(s => s.All(FontScripts.Supported.Contains)).WithMessage($"Scripts must be from: {string.Join(", ", FontScripts.Supported)}.");
        RuleFor(x => x.License).MaximumLength(200);
    }
}

internal sealed class UpdateFontRequestValidator : AppValidator<UpdateFontRequest>
{
    public UpdateFontRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Scripts).NotEmpty()
            .Must(s => s.All(FontScripts.Supported.Contains)).WithMessage($"Scripts must be from: {string.Join(", ", FontScripts.Supported)}.");
        RuleFor(x => x.License).MaximumLength(200);
    }
}

public interface IAdminFontService
{
    Task<PagedResult<FontDto>> SearchAsync(FontQuery query, CancellationToken cancellationToken = default);
    Task<Result<FontDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<FontDto>> UploadAsync(FontMetadata metadata, FileUpload woff2, FileUpload? ttf, CancellationToken cancellationToken = default);
    Task<Result<FontDto>> UpdateAsync(Guid id, UpdateFontRequest request, CancellationToken cancellationToken = default);
    Task<Result<FontDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Self-hosted font library. WOFF2 is public (browsers load it); the optional TTF stays private for server rendering.</summary>
internal sealed class AdminFontService(
    IFontRepository fonts,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    IValidator<FontMetadata> metadataValidator,
    TemplateMapper mapper,
    IAuditLogger audit) : IAdminFontService
{
    public async Task<PagedResult<FontDto>> SearchAsync(FontQuery query, CancellationToken cancellationToken = default)
    {
        var page = await fonts.SearchAsync(query, cancellationToken);
        return new PagedResult<FontDto>(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<FontDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await fonts.GetRowAsync(id, cancellationToken);
        return row is null ? TemplateErrors.FontNotFound : ToDto(row);
    }

    public async Task<Result<FontDto>> UploadAsync(FontMetadata metadata, FileUpload woff2, FileUpload? ttf, CancellationToken cancellationToken = default)
    {
        var validation = await metadataValidator.ValidateAsync(metadata, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<FontDto>(validation.Errors.Select(e =>
                Error.Validation(e.ErrorCode, e.ErrorMessage, char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])));

        var woff2Bytes = await ReadSmallFileAsync(woff2, cancellationToken);
        if (woff2Bytes is null)
            return TemplateErrors.FontTooLarge;
        if (!FontFiles.IsWoff2(woff2Bytes))
            return TemplateErrors.InvalidWoff2;

        byte[]? ttfBytes = null;
        if (ttf is not null)
        {
            ttfBytes = await ReadSmallFileAsync(ttf, cancellationToken);
            if (ttfBytes is null || !FontFiles.IsTrueTypeOrOpenType(ttfBytes))
                return TemplateErrors.InvalidTtf;
        }

        var family = metadata.FamilyName.Trim();
        if (await fonts.VariantExistsAsync(family, metadata.Weight, metadata.Style, cancellationToken))
            return TemplateErrors.FontExists;

        var font = new Font
        {
            FamilyName = family,
            DisplayName = metadata.DisplayName.Trim(),
            Weight = metadata.Weight,
            Style = metadata.Style,
            Scripts = metadata.Scripts.Distinct().ToList(),
            License = AdminCategoryServiceHelpers.Clean(metadata.License),
        };
        font.Woff2Key = TemplateStorageKeys.FontWoff2(font.Id);
        font.TtfKey = ttfBytes is null ? null : TemplateStorageKeys.FontTtf(font.Id);

        await using (var ms = new MemoryStream(woff2Bytes))
            await storage.SaveAsync(font.Woff2Key, ms, "font/woff2", cancellationToken);
        if (ttfBytes is not null)
        {
            await using var ms = new MemoryStream(ttfBytes);
            await storage.SaveAsync(font.TtfKey!, ms, "font/ttf", cancellationToken);
        }

        await fonts.AddAsync(font, cancellationToken);
        audit.Log("FontUploaded", nameof(Font), font.Id.ToString(), details: new { font.FamilyName, font.Weight, font.Style });
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(font.Woff2Key, CancellationToken.None);
            if (font.TtfKey is not null) await storage.DeleteAsync(font.TtfKey, CancellationToken.None);
            throw;
        }

        return await GetAsync(font.Id, cancellationToken);
    }

    public async Task<Result<FontDto>> UpdateAsync(Guid id, UpdateFontRequest request, CancellationToken cancellationToken = default)
    {
        var font = await fonts.GetByIdAsync(id, cancellationToken);
        if (font is null)
            return TemplateErrors.FontNotFound;

        font.DisplayName = request.DisplayName.Trim();
        font.Scripts = request.Scripts.Distinct().ToList();
        font.License = AdminCategoryServiceHelpers.Clean(request.License);
        audit.Log("FontUpdated", nameof(Font), id.ToString());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<FontDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var font = await fonts.GetByIdAsync(id, cancellationToken);
        if (font is null)
            return TemplateErrors.FontNotFound;

        if (font.IsActive != isActive)
        {
            font.IsActive = isActive;
            audit.Log(isActive ? "FontActivated" : "FontDeactivated", nameof(Font), id.ToString());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var font = await fonts.GetByIdAsync(id, cancellationToken);
        if (font is null)
            return TemplateErrors.FontNotFound;

        var usage = await fonts.CountTemplatesUsingAsync(id, cancellationToken);
        if (usage > 0)
            return TemplateErrors.FontInUse(usage);

        fonts.Remove(font);
        audit.Log("FontDeleted", nameof(Font), id.ToString(), details: new { font.FamilyName, font.Weight, font.Style });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await storage.DeleteAsync(font.Woff2Key, CancellationToken.None);
        if (font.TtfKey is not null)
            await storage.DeleteAsync(font.TtfKey, CancellationToken.None);
        return Result.Success();
    }

    private FontDto ToDto(FontRow r) => new(
        r.Id, r.FamilyName, r.DisplayName, r.Weight, r.Style, r.Scripts, mapper.FontUrl(r.Woff2Key), r.HasTtf,
        r.License, r.IsActive, r.TemplateCount, r.CreatedAt);

    /// <summary>Reads at most <see cref="FontFiles.MaxBytes"/>; null when the file is empty or larger.</summary>
    private static async Task<byte[]?> ReadSmallFileAsync(FileUpload file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > FontFiles.MaxBytes)
            return null;
        using var ms = new MemoryStream();
        await file.Content.CopyToAsync(ms, cancellationToken);
        return ms.Length is > 0 and <= FontFiles.MaxBytes ? ms.ToArray() : null;
    }
}

internal static class AdminCategoryServiceHelpers
{
    public static string? Clean(string? value) => Categories.AdminCategoryService.Clean(value);
}
