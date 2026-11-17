using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Templates;

public static class TemplateErrors
{
    public static readonly Error NotFound = Error.NotFound("Template.NotFound", "Template not found.");
    public static readonly Error AssetNotFound = Error.NotFound("TemplateAsset.NotFound", "Asset not found.");
    public static readonly Error FontNotFound = Error.NotFound("Font.NotFound", "Font not found.");

    public static readonly Error CategoryNotFound =
        new("Template.CategoryNotFound", "The selected category does not exist.", ErrorType.Validation, "categoryId");

    public static readonly Error SubCategoryMismatch =
        new("Template.SubCategoryMismatch", "The subcategory does not belong to the selected category.", ErrorType.Validation, "subCategoryId");

    public static readonly Error SlugRequired =
        Error.Validation("Template.SlugRequired", "A URL slug could not be generated from the name. Please enter one.", "slug");

    public static Error SlugTaken(string slug) =>
        new("Template.SlugTaken", $"The slug \"{slug}\" is already used by another template.", ErrorType.Conflict, "slug");

    public static Error UnknownAssets(IEnumerable<Guid> ids) =>
        Error.Validation("Template.UnknownAssets", $"The design references assets that do not belong to this template: {string.Join(", ", ids)}.", "design");

    public static Error FieldPageOutOfRange(string fieldKey, int pages) =>
        Error.Validation("Template.FieldPage", $"Field \"{fieldKey}\" is on a page that does not exist (the design has {pages} page{(pages == 1 ? "" : "s")}).", "fields");

    public static Error UnknownFonts(IEnumerable<Guid> ids) =>
        Error.Validation("Template.UnknownFonts", $"These fonts do not exist or are inactive: {string.Join(", ", ids)}.", "fonts");

    public static Error UnknownPackages(IEnumerable<Guid> ids) =>
        Error.Validation("Template.UnknownPackages", $"These packages do not exist or are inactive: {string.Join(", ", ids)}.", "prices");

    public static readonly Error FreePackageOnPremium =
        Error.Validation("Template.FreePackageOnPremium", "Premium templates cannot be sold with the Free package.", "prices");

    public static readonly Error PaidPackageOnFree =
        Error.Validation("Template.PaidPackageOnFree", "Free templates can only use the Free package (price 0).", "prices");

    public static readonly Error FreePackageMustBeZero =
        Error.Validation("Template.FreePackagePrice", "The Free package price must be 0.", "prices");

    public static Error NotReadyToPublish(IEnumerable<string> issues) =>
        Error.Validation("Template.NotReadyToPublish", "This template cannot be published yet: " + string.Join(" ", issues), "status");

    public static Error HasInvitations(int count) =>
        Error.Conflict("Template.HasInvitations",
            $"{count} invitation{(count == 1 ? " uses" : "s use")} this template, so it cannot be deleted. Archive it instead.");

    public static readonly Error AssetInUse =
        Error.Conflict("TemplateAsset.InUse", "This asset is used in the template design. Remove it from the design first.");

    public static readonly Error UnsupportedImage =
        Error.Validation("TemplateAsset.UnsupportedImage", "Only JPEG, PNG or WebP images are accepted.", "file");

    public static Error ImageTooLarge(long maxBytes) =>
        Error.Validation("TemplateAsset.TooLarge", $"Images must be at most {maxBytes / (1024 * 1024)} MB.", "file");

    public static Error ImageDimensions(int min, int max) =>
        Error.Validation("TemplateAsset.Dimensions", $"Images must be between {min} and {max} pixels on each side.", "file");

    public static readonly Error PageOutOfRange =
        Error.Validation("TemplateAsset.Page", "Page index must be 0–3.", "pageIndex");

    // Fonts
    public static readonly Error InvalidWoff2 =
        Error.Validation("Font.InvalidWoff2", "The font file must be a valid WOFF2 file.", "woff2File");

    public static readonly Error InvalidTtf =
        Error.Validation("Font.InvalidTtf", "The optional TTF/OTF file is not a valid TrueType or OpenType font.", "ttfFile");

    public static readonly Error FontTooLarge =
        Error.Validation("Font.TooLarge", "Font files must be at most 5 MB.", "woff2File");

    public static readonly Error FontExists =
        new("Font.Exists", "A font with this family, weight and style already exists.", ErrorType.Conflict, "familyName");

    public static Error FontInUse(int count) =>
        Error.Conflict("Font.InUse", $"This font is used by {count} template{(count == 1 ? "" : "s")}. Deactivate it instead.");
}
