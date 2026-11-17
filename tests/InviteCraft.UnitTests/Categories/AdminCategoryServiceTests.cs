using System.Linq.Expressions;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Enums;
using NSubstitute;

namespace InviteCraft.UnitTests.Categories;

public class AdminCategoryServiceTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ISubCategoryRepository _subCategories = Substitute.For<ISubCategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AdminCategoryService _categoryService;
    private readonly AdminSubCategoryService _subCategoryService;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AdminCategoryServiceTests()
    {
        var audit = Substitute.For<IAuditLogger>();
        _categoryService = new AdminCategoryService(_categories, _subCategories, _unitOfWork, audit);
        _subCategoryService = new AdminSubCategoryService(_subCategories, _categories, _unitOfWork, audit);
    }

    private static SaveCategoryRequest Request(string name, string? slug = null) =>
        new(name, slug, CategoryKind.Festival, null, "bi-stars", null, 10, true, true, null, null);

    [Fact]
    public async Task Create_generates_slug_from_name_when_none_given()
    {
        Category? added = null;
        await _categories.AddAsync(Arg.Do<Category>(c => added = c), Arg.Any<CancellationToken>());

        await _categoryService.CreateAsync(Request("Onam Festival"), Ct);

        added.ShouldNotBeNull();
        added.Slug.ShouldBe("onam-festival");
        added.Icon.ShouldBe("bi-stars");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_taken_slug_returns_conflict_on_slug_field()
    {
        _categories.SlugExistsAsync("diwali", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _categoryService.CreateAsync(Request("Diwali Specials", "Diwali"), Ct);

        result.FirstError!.Code.ShouldBe("Category.SlugTaken");
        result.FirstError.Field.ShouldBe("slug");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_non_latin_name_and_no_slug_asks_for_a_slug()
    {
        var result = await _categoryService.CreateAsync(Request("दिवाली"), Ct);

        result.FirstError.ShouldBe(CategoryErrors.SlugRequired);
    }

    [Fact]
    public async Task Delete_is_refused_while_templates_exist()
    {
        var category = new Category { Name = "Wedding", Slug = "wedding" };
        _categories.GetWithSubCategoriesAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _categories.CountTemplatesAsync(category.Id, Arg.Any<CancellationToken>()).Returns(3);

        var result = await _categoryService.DeleteAsync(category.Id, Ct);

        result.FirstError!.Code.ShouldBe("Category.HasTemplates");
        _categories.DidNotReceive().Remove(Arg.Any<Category>());
    }

    [Fact]
    public async Task Delete_removes_category_and_its_subcategories()
    {
        var category = new Category { Name = "Other", Slug = "other" };
        category.SubCategories.Add(new SubCategory { Name = "Graduation", Slug = "graduation" });
        category.SubCategories.Add(new SubCategory { Name = "Retirement", Slug = "retirement" });
        _categories.GetWithSubCategoriesAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _categoryService.DeleteAsync(category.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        _categories.Received(1).Remove(category);
        _subCategories.Received(2).Remove(Arg.Any<SubCategory>());
    }

    [Fact]
    public async Task Reorder_with_unknown_ids_is_rejected()
    {
        var known = new Category { Name = "A", Slug = "a" };
        _categories.GetTrackedByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([known]);

        var result = await _categoryService.ReorderAsync(
            new ReorderRequest([new SortOrderItem(known.Id, 5), new SortOrderItem(Guid.NewGuid(), 6)]), Ct);

        result.FirstError!.Code.ShouldBe("Category.ReorderNotFound");
        known.SortOrder.ShouldBe(0);
    }

    [Fact]
    public async Task Subcategory_cannot_be_created_under_missing_category()
    {
        _categories.ExistsAsync(Arg.Any<Expression<Func<Category, bool>>>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _subCategoryService.CreateAsync(new SaveSubCategoryRequest(Guid.NewGuid(), "Onam", null, null, null, 0, true), Ct);

        result.FirstError!.Field.ShouldBe("categoryId");
    }

    [Fact]
    public async Task Subcategory_with_templates_cannot_move_to_another_category()
    {
        var sub = new SubCategory { CategoryId = Guid.NewGuid(), Name = "Wedding", Slug = "wedding" };
        _subCategories.GetByIdAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);
        _categories.ExistsAsync(Arg.Any<Expression<Func<Category, bool>>>(), Arg.Any<CancellationToken>()).Returns(true);
        _subCategories.CountTemplatesAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(2);

        var result = await _subCategoryService.UpdateAsync(sub.Id,
            new SaveSubCategoryRequest(Guid.NewGuid(), "Wedding", "wedding", null, null, 0, true), Ct);

        result.FirstError!.Code.ShouldBe("SubCategory.CannotMove");
    }

    [Fact]
    public async Task Same_subcategory_slug_is_allowed_in_different_categories()
    {
        var parent = Guid.NewGuid();
        _categories.ExistsAsync(Arg.Any<Expression<Func<Category, bool>>>(), Arg.Any<CancellationToken>()).Returns(true);
        _subCategories.SlugExistsAsync(parent, "wedding", null, Arg.Any<CancellationToken>()).Returns(false);

        await _subCategoryService.CreateAsync(new SaveSubCategoryRequest(parent, "Wedding", null, null, null, 0, true), Ct);

        await _subCategories.Received(1).SlugExistsAsync(parent, "wedding", null, Arg.Any<CancellationToken>());
        await _subCategories.Received(1).AddAsync(Arg.Is<SubCategory>(s => s.CategoryId == parent && s.Slug == "wedding"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_rejects_bad_slug_icon_and_url()
    {
        var result = new SaveCategoryRequestValidator().Validate(
            new SaveCategoryRequest("Diwali", "Diwali Specials", CategoryKind.Festival, null, "flower", "ftp://x", -1, true, true, null, null));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["Slug", "Icon", "ImageUrl", "SortOrder"], ignoreOrder: true);
    }
}
