using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.IntegrationTests.Persistence;

/// <summary>
/// Regression test: children added through a navigation collection of a tracked entity must be
/// INSERTed, even though their UUID v7 key is already set by the application.
/// </summary>
public class KeyGenerationTests
{
    [Fact]
    public void New_child_added_to_tracked_parent_collection_is_tracked_as_Added()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new AppDbContext(options);

        var template = new Template { Name = "T", Category = new Category { Name = "C", Slug = "c" } };
        context.Attach(template);                     // simulates an entity loaded from the database
        template.Assets.Add(new TemplateAsset());     // key already set (Guid.CreateVersion7)
        template.EditableFields.Add(new TemplateEditableField { FieldKey = "bride_name" });

        context.ChangeTracker.DetectChanges();

        context.ChangeTracker.Entries<TemplateAsset>().Single().State.ShouldBe(EntityState.Added);
        context.ChangeTracker.Entries<TemplateEditableField>().Single().State.ShouldBe(EntityState.Added);
    }
}
