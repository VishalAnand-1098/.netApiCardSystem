using InviteCraft.Domain.Common;
using InviteCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace InviteCraft.IntegrationTests.Persistence;

/// <summary>
/// Guards the EF Core model without needing a database: naming, conventions and seed data.
/// (Database round-trip tests with Testcontainers arrive in Phase 13.)
/// </summary>
public class ModelTests
{
    private static readonly IModel Model = CreateDesignTimeModel();

    private static IModel CreateDesignTimeModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only") // never opened
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new AppDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Model_maps_all_31_tables_with_snake_case_names()
    {
        var tables = Model.GetEntityTypes().Select(t => t.GetTableName()!).Distinct().ToList();

        tables.Count.ShouldBe(31);
        tables.ShouldAllBe(name => name == name.ToLowerInvariant());
        tables.ShouldContain("template_editable_fields");
        tables.ShouldContain("payment_webhook_events");
    }

    [Fact]
    public void Every_soft_deletable_entity_has_a_query_filter()
    {
        var softDeletable = Model.GetEntityTypes()
            .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType))
            .ToList();

        softDeletable.ShouldNotBeEmpty();
        softDeletable.ShouldAllBe(t => t.GetDeclaredQueryFilters().Count > 0);
    }

    [Fact]
    public void Every_enum_is_stored_as_string_with_a_check_constraint()
    {
        foreach (var entityType in Model.GetEntityTypes())
        {
            var enumProperties = entityType.GetProperties()
                .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum);

            foreach (var property in enumProperties)
            {
                property.GetProviderClrType().ShouldBe(typeof(string), $"{entityType.DisplayName()}.{property.Name}");
                var column = property.GetColumnName();
                entityType.GetCheckConstraints()
                    .ShouldContain(c => c.Name == $"ck_{entityType.GetTableName()}_{column}",
                        $"missing CHECK constraint for {entityType.DisplayName()}.{property.Name}");
            }
        }
    }

    [Fact]
    public void Money_columns_use_numeric_12_2()
    {
        var order = Model.FindEntityType(typeof(Domain.Entities.Commerce.Order))!;

        order.FindProperty(nameof(Domain.Entities.Commerce.Order.TotalAmount))!.GetColumnType().ShouldBe("numeric(12,2)");
    }

    [Fact]
    public void Seed_data_contains_all_categories_and_subcategories_from_the_brief()
    {
        var categories = Model.FindEntityType(typeof(Domain.Entities.Catalog.Category))!.GetSeedData().ToList();
        var subCategories = Model.FindEntityType(typeof(Domain.Entities.Catalog.SubCategory))!.GetSeedData().ToList();
        var packages = Model.FindEntityType(typeof(Domain.Entities.Templates.Package))!.GetSeedData().ToList();
        var roles = Model.FindEntityType(typeof(Domain.Entities.Identity.Role))!.GetSeedData().ToList();

        categories.Count.ShouldBe(7);
        subCategories.Count.ShouldBe(34);
        packages.Count.ShouldBe(5);
        roles.Select(r => r["Name"]).ShouldBe([RoleNames.Admin, RoleNames.Customer], ignoreOrder: true);

        // Slugs are unique within each category ("wedding" appears under several categories).
        subCategories
            .GroupBy(s => (s["CategoryId"], s["Slug"]))
            .ShouldAllBe(g => g.Count() == 1);
    }
}
