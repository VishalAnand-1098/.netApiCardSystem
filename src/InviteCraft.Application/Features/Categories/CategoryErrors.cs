using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Categories;

public static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("Category.NotFound", "Category not found.");

    public static readonly Error SubCategoryNotFound = Error.NotFound("SubCategory.NotFound", "Subcategory not found.");

    public static readonly Error ParentNotFound =
        new("Category.NotFound", "The selected category does not exist.", ErrorType.Validation, "categoryId");

    public static readonly Error SlugRequired =
        Error.Validation("Category.SlugRequired", "A URL slug could not be generated from the name. Please enter one (e.g. \"diwali\").", "slug");

    public static Error SlugTaken(string slug) =>
        new("Category.SlugTaken", $"The slug \"{slug}\" is already used by another category.", ErrorType.Conflict, "slug");

    public static Error SubCategorySlugTaken(string slug) =>
        new("SubCategory.SlugTaken", $"The slug \"{slug}\" is already used in this category.", ErrorType.Conflict, "slug");

    public static Error HasTemplates(int count) =>
        Error.Conflict("Category.HasTemplates",
            $"This category still has {count} template{(count == 1 ? "" : "s")}. Move or delete them first, or deactivate the category instead.");

    public static Error SubCategoryHasTemplates(int count) =>
        Error.Conflict("SubCategory.HasTemplates",
            $"This subcategory still has {count} template{(count == 1 ? "" : "s")}. Move or delete them first, or deactivate it instead.");

    public static Error CannotMoveWithTemplates(int count) =>
        new("SubCategory.CannotMove",
            $"This subcategory has {count} template{(count == 1 ? "" : "s")}, so it cannot be moved to another category.",
            ErrorType.Conflict, "categoryId");

    public static Error ReorderItemsNotFound(int count) =>
        Error.NotFound("Category.ReorderNotFound", $"{count} of the items to reorder were not found.");
}
