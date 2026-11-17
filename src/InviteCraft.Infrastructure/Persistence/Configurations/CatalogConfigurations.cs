using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InviteCraft.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Icon).HasMaxLength(64);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024);
        builder.Property(x => x.SeoTitle).HasMaxLength(160);
        builder.Property(x => x.SeoDescription).HasMaxLength(320);

        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        builder.HasData(SeedData.Categories);
    }
}

internal sealed class SubCategoryConfiguration : IEntityTypeConfiguration<SubCategory>
{
    public void Configure(EntityTypeBuilder<SubCategory> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024);

        // Unique within the category: "wedding" exists under Hindu, Christian and Wedding.
        builder.HasIndex(x => new { x.CategoryId, x.Slug }).IsUnique().HasFilter("is_deleted = false");

        builder.HasOne(x => x.Category).WithMany(c => c.SubCategories).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasData(SeedData.SubCategories);
    }
}

internal sealed class FestivalCollectionConfiguration : IEntityTypeConfiguration<FestivalCollection>
{
    public void Configure(EntityTypeBuilder<FestivalCollection> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.BannerImageUrl).HasMaxLength(1024);
        builder.Property(x => x.ThemeColor).HasMaxLength(16);

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_festival_collections_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at"));
    }
}

internal sealed class FestivalCollectionTemplateConfiguration : IEntityTypeConfiguration<FestivalCollectionTemplate>
{
    public void Configure(EntityTypeBuilder<FestivalCollectionTemplate> builder)
    {
        builder.HasKey(x => new { x.CollectionId, x.TemplateId });

        builder.HasOne(x => x.Collection).WithMany(c => c.Templates).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}
