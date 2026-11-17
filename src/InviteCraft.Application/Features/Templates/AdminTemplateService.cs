using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Templates;

public interface IAdminTemplateService
{
    Task<PagedResult<AdminTemplateListItemDto>> SearchAsync(AdminTemplateQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> CreateAsync(SaveTemplateRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> UpdateAsync(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> SaveDesignAsync(Guid id, SaveTemplateDesignRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> SaveFieldsAsync(Guid id, SaveTemplateFieldsRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> SaveFontsAsync(Guid id, SaveTemplateFontsRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> SavePricingAsync(Guid id, SaveTemplatePricingRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminTemplateDto>> SetStatusAsync(Guid id, TemplateStatus status, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class AdminTemplateService(
    ITemplateRepository templates,
    ICategoryRepository categories,
    ISubCategoryRepository subCategories,
    IUnitOfWork unitOfWork,
    TemplateMapper mapper,
    IAuditLogger audit,
    TimeProvider timeProvider) : IAdminTemplateService
{
    public async Task<PagedResult<AdminTemplateListItemDto>> SearchAsync(AdminTemplateQuery query, CancellationToken cancellationToken = default)
    {
        var page = await templates.SearchAdminAsync(query, cancellationToken);
        return new PagedResult<AdminTemplateListItemDto>(page.Items.Select(mapper.ToAdminListItem).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<AdminTemplateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: false, cancellationToken);
        return template is null ? TemplateErrors.NotFound : await ToAdminDtoAsync(template, cancellationToken);
    }

    // ------------------------------------------------------------------ metadata

    public async Task<Result<AdminTemplateDto>> CreateAsync(SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var check = await ValidateMetadataAsync(request, excludeId: null, cancellationToken);
        if (check.IsFailure)
            return Result.Failure<AdminTemplateDto>(check.Errors);

        var template = new Template
        {
            Code = await templates.NextCodeAsync(cancellationToken),
            Status = TemplateStatus.Draft,
            Design = TemplateJson.Serialize(TemplateDesign.Empty()),
            StartingPrice = request.Tier == TemplateTier.Free ? 0m : null,
        };
        ApplyMetadata(template, request, check.Value);
        await templates.AddAsync(template, cancellationToken);

        audit.Log("TemplateCreated", nameof(Template), template.Id.ToString(), details: new { template.Code, template.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(template.Id, cancellationToken);
    }

    public async Task<Result<AdminTemplateDto>> UpdateAsync(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var check = await ValidateMetadataAsync(request, id, cancellationToken);
        if (check.IsFailure)
            return Result.Failure<AdminTemplateDto>(check.Errors);

        var tierChanged = template.Tier != request.Tier;
        ApplyMetadata(template, request, check.Value);
        if (tierChanged)
        {
            // Drop prices that no longer fit the tier (Free ↔ Premium) and recompute the "from" price.
            foreach (var price in template.Pricing.Where(p => (p.Package.Code == Package.FreeCode) != (template.Tier == TemplateTier.Free)).ToList())
                template.Pricing.Remove(price);
            RecomputeStartingPrice(template);
        }

        audit.Log("TemplateUpdated", nameof(Template), id.ToString(), details: new { template.Name, template.Slug, template.Tier });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ------------------------------------------------------------------ design, fields, fonts, pricing

    public async Task<Result<AdminTemplateDto>> SaveDesignAsync(Guid id, SaveTemplateDesignRequest request, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var design = request.Design;
        var ownAssets = template.Assets.Select(a => a.Id).ToHashSet();
        var unknownAssets = design.ReferencedAssetIds().Where(a => !ownAssets.Contains(a)).Distinct().ToList();
        if (unknownAssets.Count > 0)
            return TemplateErrors.UnknownAssets(unknownAssets);

        var fontCheck = await CheckFontsExistAsync(design.ReferencedFontIds(), cancellationToken);
        if (fontCheck is not null)
            return fontCheck;

        var pageCount = design.Pages.Count;
        if (template.EditableFields.FirstOrDefault(f => f.IsActive && f.PageIndex >= pageCount) is { } orphan)
            return TemplateErrors.FieldPageOutOfRange(orphan.FieldKey, pageCount);

        template.Design = TemplateJson.Serialize(design with { Pages = design.Pages.OrderBy(p => p.Index).ToList() });
        BumpLayoutVersion(template);

        audit.Log("TemplateDesignSaved", nameof(Template), id.ToString(),
            details: new { pages = pageCount, layers = design.Pages.Sum(p => p.Layers.Count) });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<AdminTemplateDto>> SaveFieldsAsync(Guid id, SaveTemplateFieldsRequest request, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var pageCount = (TemplateJson.Deserialize<TemplateDesign>(template.Design) ?? TemplateDesign.Empty()).Pages.Count;
        if (request.Fields.FirstOrDefault(f => f.PageIndex >= pageCount) is { } badPage)
            return TemplateErrors.FieldPageOutOfRange(badPage.FieldKey, pageCount);

        var fontIds = request.Fields.Where(f => f.Style?.FontId is not null).Select(f => f.Style!.FontId!.Value);
        var fontCheck = await CheckFontsExistAsync(fontIds, cancellationToken);
        if (fontCheck is not null)
            return fontCheck;

        var existing = template.EditableFields.ToDictionary(f => f.FieldKey);
        var incomingKeys = request.Fields.Select(f => f.FieldKey).ToHashSet();

        // Removed fields: delete if no invitation uses them, otherwise keep them inactive (history stays valid).
        var removed = existing.Values.Where(f => !incomingKeys.Contains(f.FieldKey)).ToList();
        var inUse = await templates.GetFieldIdsWithValuesAsync(removed.Select(f => f.Id).ToList(), cancellationToken);
        foreach (var field in removed)
        {
            if (inUse.Contains(field.Id))
                field.IsActive = false;
            else
                template.EditableFields.Remove(field);
        }

        foreach (var input in request.Fields)
        {
            if (!existing.TryGetValue(input.FieldKey, out var field))
            {
                field = new TemplateEditableField { TemplateId = template.Id, FieldKey = input.FieldKey };
                template.EditableFields.Add(field);
            }
            ApplyField(field, input);
        }

        BumpLayoutVersion(template);
        audit.Log("TemplateFieldsSaved", nameof(Template), id.ToString(),
            details: new { fields = request.Fields.Count, removed = removed.Count, deactivated = inUse.Count });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<AdminTemplateDto>> SaveFontsAsync(Guid id, SaveTemplateFontsRequest request, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var fontCheck = await CheckFontsExistAsync(request.Fonts.Select(f => f.FontId), cancellationToken);
        if (fontCheck is not null)
            return fontCheck;

        var incoming = request.Fonts.ToDictionary(f => f.FontId);
        foreach (var config in template.FontConfigurations.Where(c => !incoming.ContainsKey(c.FontId)).ToList())
            template.FontConfigurations.Remove(config);

        foreach (var input in request.Fonts)
        {
            var config = template.FontConfigurations.FirstOrDefault(c => c.FontId == input.FontId);
            if (config is null)
            {
                config = new TemplateFontConfiguration { TemplateId = template.Id, FontId = input.FontId };
                template.FontConfigurations.Add(config);
            }
            config.Role = input.Role;
            config.IsDefault = input.IsDefault;
            config.SortOrder = input.SortOrder;
        }

        BumpLayoutVersion(template);
        audit.Log("TemplateFontsSaved", nameof(Template), id.ToString(), details: new { fonts = request.Fonts.Count });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<AdminTemplateDto>> SavePricingAsync(Guid id, SaveTemplatePricingRequest request, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var packages = (await templates.GetActivePackagesAsync(cancellationToken)).ToDictionary(p => p.Id);
        var unknown = request.Prices.Where(p => !packages.ContainsKey(p.PackageId)).Select(p => p.PackageId).ToList();
        if (unknown.Count > 0)
            return TemplateErrors.UnknownPackages(unknown);

        foreach (var input in request.Prices)
        {
            var isFreePackage = packages[input.PackageId].Code == Package.FreeCode;
            if (template.Tier == TemplateTier.Premium && isFreePackage)
                return TemplateErrors.FreePackageOnPremium;
            if (template.Tier == TemplateTier.Free && !isFreePackage)
                return TemplateErrors.PaidPackageOnFree;
            if (isFreePackage && input.Price != 0)
                return TemplateErrors.FreePackageMustBeZero;
        }

        var incoming = request.Prices.ToDictionary(p => p.PackageId);
        foreach (var price in template.Pricing.Where(p => !incoming.ContainsKey(p.PackageId)).ToList())
            template.Pricing.Remove(price);

        foreach (var input in request.Prices)
        {
            var price = template.Pricing.FirstOrDefault(p => p.PackageId == input.PackageId);
            if (price is null)
            {
                // Only the FK: the package object came from a no-tracking query and must not be re-inserted.
                price = new TemplatePricing { TemplateId = template.Id, PackageId = input.PackageId };
                template.Pricing.Add(price);
            }
            price.Price = decimal.Round(input.Price, 2);
            price.CompareAtPrice = input.CompareAtPrice is { } c ? decimal.Round(c, 2) : null;
            price.IsActive = input.IsActive;
        }

        RecomputeStartingPrice(template);
        audit.Log("TemplatePricingSaved", nameof(Template), id.ToString(),
            details: request.Prices.Select(p => new { p.PackageId, p.Price, p.IsActive }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ------------------------------------------------------------------ lifecycle

    public async Task<Result<AdminTemplateDto>> SetStatusAsync(Guid id, TemplateStatus status, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(id, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        if (template.Status == status)
            return await GetAsync(id, cancellationToken);

        if (status == TemplateStatus.Published)
        {
            var issues = PublishReadiness.Check(template);
            if (issues.Count > 0)
                return TemplateErrors.NotReadyToPublish(issues);
            template.PublishedAt ??= timeProvider.GetUtcNow();
        }

        var previous = template.Status;
        template.Status = status;
        audit.Log("TemplateStatusChanged", nameof(Template), id.ToString(), details: new { from = previous, to = status });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var invitations = await templates.CountInvitationsAsync(id, cancellationToken);
        if (invitations > 0)
            return TemplateErrors.HasInvitations(invitations);

        templates.Remove(template); // soft delete; files are kept so the template can be restored
        audit.Log("TemplateDeleted", nameof(Template), id.ToString(), details: new { template.Code, template.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ------------------------------------------------------------------ helpers

    private sealed record MetadataCheck(string Slug);

    private async Task<Result<MetadataCheck>> ValidateMetadataAsync(SaveTemplateRequest request, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (!await categories.ExistsAsync(c => c.Id == request.CategoryId, cancellationToken))
            return TemplateErrors.CategoryNotFound;

        if (request.SubCategoryId is { } subId &&
            !await subCategories.ExistsAsync(s => s.Id == subId && s.CategoryId == request.CategoryId, cancellationToken))
            return TemplateErrors.SubCategoryMismatch;

        var slug = AdminCategoryService.ResolveSlug(request.Slug, request.Name);
        if (slug is null)
            return TemplateErrors.SlugRequired;
        if (await templates.SlugExistsAsync(slug, excludeId, cancellationToken))
            return TemplateErrors.SlugTaken(slug);

        return new MetadataCheck(slug);
    }

    private static void ApplyMetadata(Template template, SaveTemplateRequest request, MetadataCheck check)
    {
        template.Name = request.Name.Trim();
        template.Slug = check.Slug;
        template.CategoryId = request.CategoryId;
        template.SubCategoryId = request.SubCategoryId;
        template.Description = AdminCategoryService.Clean(request.Description);
        template.Tier = request.Tier;
        template.WidthMm = decimal.Round(request.WidthMm, 2);
        template.HeightMm = decimal.Round(request.HeightMm, 2);
        template.Orientation = template.WidthMm == template.HeightMm ? TemplateOrientation.Square
            : template.WidthMm > template.HeightMm ? TemplateOrientation.Landscape : TemplateOrientation.Portrait;
        template.Dpi = request.Dpi;
        template.BleedMm = decimal.Round(request.BleedMm, 2);
        template.SupportedLanguages = (request.SupportedLanguages ?? ["en"]).Select(l => l.Trim().ToLowerInvariant()).Distinct().ToList();
        template.Tags = (request.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        template.IsFeatured = request.IsFeatured;
    }

    private static void ApplyField(TemplateEditableField field, TemplateFieldInput input)
    {
        field.Label = input.Label.Trim();
        field.LabelTranslations = input.LabelTranslations is { Count: > 0 } t ? TemplateJson.Serialize(t) : null;
        field.FieldType = input.FieldType;
        field.GroupName = AdminCategoryService.Clean(input.GroupName);
        field.IsRequired = input.IsRequired;
        field.MaxLength = input.MaxLength;
        field.Placeholder = AdminCategoryService.Clean(input.Placeholder);
        field.DefaultValue = input.DefaultValue;
        field.Options = input.Options is { Count: > 0 } o ? TemplateJson.Serialize(o) : null;
        field.DisplayFormat = AdminCategoryService.Clean(input.DisplayFormat);
        field.PageIndex = input.PageIndex;
        field.X = input.Frame.X;
        field.Y = input.Frame.Y;
        field.Width = input.Frame.W;
        field.Height = input.Frame.H;
        field.Rotation = input.Frame.Rotation;
        field.ZIndex = input.ZIndex;
        field.Style = TemplateJson.Serialize(input.Style);
        field.Permissions = TemplateJson.Serialize(input.Permissions ?? FieldPermissions.Default);
        field.SortOrder = input.SortOrder;
        field.IsActive = input.IsActive;
    }

    private async Task<Error?> CheckFontsExistAsync(IEnumerable<Guid> fontIds, CancellationToken cancellationToken)
    {
        var ids = fontIds.Distinct().ToList();
        if (ids.Count == 0)
            return null;
        var found = (await templates.GetActiveFontsAsync(ids, cancellationToken)).Select(f => f.Id).ToHashSet();
        var missing = ids.Where(i => !found.Contains(i)).ToList();
        return missing.Count > 0 ? TemplateErrors.UnknownFonts(missing) : null;
    }

    /// <summary>Changes to a live template invalidate existing invitations' layout snapshot.</summary>
    private static void BumpLayoutVersion(Template template)
    {
        if (template.Status == TemplateStatus.Published)
            template.LayoutVersion++;
    }

    private static void RecomputeStartingPrice(Template template)
    {
        var active = template.Pricing.Where(p => p.IsActive).Select(p => p.Price).ToList();
        template.StartingPrice = active.Count > 0 ? active.Min() : template.Tier == TemplateTier.Free ? 0m : null;
    }

    private async Task<AdminTemplateDto> ToAdminDtoAsync(Template template, CancellationToken cancellationToken)
    {
        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        return new AdminTemplateDto(
            mapper.ToDetail(template, packages, includeInactive: true),
            template.Status, template.IsFeatured, template.ViewCount, template.SalesCount,
            template.CreatedAt, template.UpdatedAt,
            PublishReadiness.Check(template));
    }
}

/// <summary>Everything that must be true before a template can go live. Shown to admins as a checklist.</summary>
public static class PublishReadiness
{
    public static IReadOnlyList<string> Check(Template t)
    {
        var issues = new List<string>();
        var design = TemplateJson.Deserialize<TemplateDesign>(t.Design) ?? TemplateDesign.Empty();
        var activeFields = t.EditableFields.Where(f => f.IsActive).ToList();
        var templateFonts = t.FontConfigurations.Where(c => c.Font.IsActive).Select(c => c.FontId).ToHashSet();

        if (!t.Category.IsActive)
            issues.Add("Its category is inactive.");
        if (t.SubCategory is { IsActive: false })
            issues.Add("Its subcategory is inactive.");
        if (!t.Assets.Any(a => a.AssetType == TemplateAssetType.Thumbnail))
            issues.Add("Upload a thumbnail image.");

        foreach (var page in design.Pages)
        {
            if (page.Background?.AssetId is null && page.Background?.Color is null && design.Canvas.BackgroundColor is null)
                issues.Add($"Give page {page.Index + 1} a background image or colour.");
        }

        var ownAssets = t.Assets.Select(a => a.Id).ToHashSet();
        if (design.ReferencedAssetIds().Any(a => !ownAssets.Contains(a)))
            issues.Add("The design references deleted assets.");

        if (activeFields.Count == 0)
            issues.Add("Add at least one editable field.");
        if (activeFields.Any(f => f.PageIndex >= design.Pages.Count))
            issues.Add("Some fields are on pages that do not exist.");

        if (templateFonts.Count == 0)
            issues.Add("Add at least one font to the template.");

        var usedFonts = design.ReferencedFontIds()
            .Concat(activeFields.Select(f => TemplateJson.Deserialize<TextStyle>(f.Style)?.FontId).OfType<Guid>())
            .Distinct();
        if (usedFonts.Any(f => !templateFonts.Contains(f)))
            issues.Add("Add every font used by the design and fields to the template's font list.");

        var needsDefault = activeFields.Any(f => f.FieldType != EditableFieldType.Image && TemplateJson.Deserialize<TextStyle>(f.Style)?.FontId is null);
        if (needsDefault && !t.FontConfigurations.Any(c => c.IsDefault))
            issues.Add("Mark a default font (some fields don't specify one).");

        if (t.Tier == TemplateTier.Premium && !t.Pricing.Any(p => p.IsActive))
            issues.Add("Set at least one active price for this premium template.");

        return issues;
    }
}
