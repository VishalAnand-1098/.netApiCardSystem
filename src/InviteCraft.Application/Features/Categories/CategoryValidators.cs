using InviteCraft.Application.Common.Validation;
using FluentValidation;
using InviteCraft.Application.Common.Text;

namespace InviteCraft.Application.Features.Categories;

internal static class CategoryRules
{
    public const int MaxSortOrder = 100_000;

    public static IRuleBuilderOptions<T, string?> OptionalSlug<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(slug => string.IsNullOrWhiteSpace(slug) || SlugGenerator.IsValid(slug.Trim()))
            .WithMessage("Slug may contain only lowercase letters, numbers and single hyphens (e.g. \"griha-pravesh\").");

    public static IRuleBuilderOptions<T, string?> OptionalHttpUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(1024)
            .Must(url => string.IsNullOrWhiteSpace(url)
                         || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                         || url.StartsWith('/'))
            .WithMessage("Enter an absolute http(s) URL or a site-relative path starting with '/'.");
}

internal sealed class SaveCategoryRequestValidator : AppValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.")
            .Must(n => n.Trim().Length >= 2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(100);
        RuleFor(x => x.Slug).OptionalSlug();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Icon).MaximumLength(64)
            .Matches("^bi-[a-z0-9-]+$").When(x => !string.IsNullOrWhiteSpace(x.Icon))
            .WithMessage("Icon must be a Bootstrap Icons class, e.g. \"bi-flower1\".");
        RuleFor(x => x.ImageUrl).OptionalHttpUrl();
        RuleFor(x => x.SortOrder).InclusiveBetween(0, CategoryRules.MaxSortOrder);
        RuleFor(x => x.SeoTitle).MaximumLength(160);
        RuleFor(x => x.SeoDescription).MaximumLength(320);
    }
}

internal sealed class SaveSubCategoryRequestValidator : AppValidator<SaveSubCategoryRequest>
{
    public SaveSubCategoryRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Category is required.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.")
            .Must(n => n.Trim().Length >= 2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(100);
        RuleFor(x => x.Slug).OptionalSlug();
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ImageUrl).OptionalHttpUrl();
        RuleFor(x => x.SortOrder).InclusiveBetween(0, CategoryRules.MaxSortOrder);
    }
}

internal sealed class ReorderRequestValidator : AppValidator<ReorderRequest>
{
    public ReorderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.")
            .Must(items => items.Count <= 500).WithMessage("At most 500 items can be reordered at once.")
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count).WithMessage("Each item may appear only once.");
        RuleForEach(x => x.Items).Cascade(CascadeMode.Continue).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty();
            item.RuleFor(i => i.SortOrder).InclusiveBetween(0, CategoryRules.MaxSortOrder);
        });
    }
}
