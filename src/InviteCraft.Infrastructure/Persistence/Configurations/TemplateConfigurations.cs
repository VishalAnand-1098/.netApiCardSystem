using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InviteCraft.Infrastructure.Persistence.Configurations;

internal sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.WidthMm).HasPrecision(7, 2);
        builder.Property(x => x.HeightMm).HasPrecision(7, 2);
        builder.Property(x => x.BleedMm).HasPrecision(5, 2);
        builder.Property(x => x.AvgRating).HasPrecision(3, 2);
        builder.Property(x => x.Design).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(x => new { x.Status, x.CategoryId });
        builder.HasIndex(x => x.SubCategoryId);
        builder.HasIndex(x => new { x.Status, x.PublishedAt }).IsDescending(false, true);   // "Recently added"
        builder.HasIndex(x => new { x.Status, x.SalesCount }).IsDescending(false, true);    // "Best-selling"
        builder.HasIndex(x => x.SupportedLanguages).HasMethod("gin");
        builder.HasIndex(x => x.Tags).HasMethod("gin");
        builder.HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_templates_name_trgm");

        builder.HasOne(x => x.Category).WithMany(c => c.Templates).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubCategory).WithMany(s => s.Templates).HasForeignKey(x => x.SubCategoryId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_templates_dimensions", "width_mm > 0 AND height_mm > 0 AND bleed_mm >= 0");
            t.HasCheckConstraint("ck_templates_dpi", "dpi BETWEEN 72 AND 1200");
            t.HasCheckConstraint("ck_templates_avg_rating", "avg_rating BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_templates_starting_price", "starting_price IS NULL OR starting_price >= 0");
        });
    }
}

internal sealed class TemplateAssetConfiguration : IEntityTypeConfiguration<TemplateAsset>
{
    public void Configure(EntityTypeBuilder<TemplateAsset> builder)
    {
        builder.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.PublicUrl).HasMaxLength(1024);
        builder.Property(x => x.MimeType).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.TemplateId, x.AssetType });

        builder.HasOne(x => x.Template).WithMany(t => t.Assets).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_template_assets_size", "size_bytes >= 0"));
    }
}

internal sealed class TemplateEditableFieldConfiguration : IEntityTypeConfiguration<TemplateEditableField>
{
    public void Configure(EntityTypeBuilder<TemplateEditableField> builder)
    {
        builder.Property(x => x.FieldKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LabelTranslations).HasColumnType("jsonb");
        builder.Property(x => x.GroupName).HasMaxLength(60);
        builder.Property(x => x.Placeholder).HasMaxLength(200);
        builder.Property(x => x.DefaultValue).HasMaxLength(1000);
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.DisplayFormat).HasMaxLength(60);
        builder.Property(x => x.X).HasPrecision(7, 6);
        builder.Property(x => x.Y).HasPrecision(7, 6);
        builder.Property(x => x.Width).HasPrecision(7, 6);
        builder.Property(x => x.Height).HasPrecision(7, 6);
        builder.Property(x => x.Rotation).HasPrecision(6, 2);
        builder.Property(x => x.Style).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Permissions).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.TemplateId, x.FieldKey }).IsUnique();

        builder.HasOne(x => x.Template).WithMany(t => t.EditableFields).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_template_editable_fields_frame",
                "x BETWEEN 0 AND 1 AND y BETWEEN 0 AND 1 AND width > 0 AND width <= 1 AND height > 0 AND height <= 1");
            t.HasCheckConstraint("ck_template_editable_fields_max_length", "max_length IS NULL OR max_length > 0");
        });
    }
}

internal sealed class FontConfiguration : IEntityTypeConfiguration<Font>
{
    public void Configure(EntityTypeBuilder<Font> builder)
    {
        builder.Property(x => x.FamilyName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Style).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Woff2Key).HasMaxLength(512).IsRequired();
        builder.Property(x => x.TtfKey).HasMaxLength(512);
        builder.Property(x => x.License).HasMaxLength(200);

        builder.HasIndex(x => new { x.FamilyName, x.Weight, x.Style }).IsUnique();
        builder.HasIndex(x => x.Scripts).HasMethod("gin");

        builder.ToTable(t => t.HasCheckConstraint("ck_fonts_weight", "weight BETWEEN 100 AND 900"));
    }
}

internal sealed class TemplateFontConfigurationConfiguration : IEntityTypeConfiguration<TemplateFontConfiguration>
{
    public void Configure(EntityTypeBuilder<TemplateFontConfiguration> builder)
    {
        builder.HasIndex(x => new { x.TemplateId, x.FontId }).IsUnique();

        builder.HasOne(x => x.Template).WithMany(t => t.FontConfigurations).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Font).WithMany().HasForeignKey(x => x.FontId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Features).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_packages_base_price", "base_price >= 0");
            t.HasCheckConstraint("ck_packages_max_dpi", "max_dpi BETWEEN 72 AND 1200");
            t.HasCheckConstraint("ck_packages_max_downloads", "max_downloads IS NULL OR max_downloads > 0");
        });

        builder.HasData(SeedData.Packages);
    }
}

internal sealed class TemplatePricingConfiguration : IEntityTypeConfiguration<TemplatePricing>
{
    public void Configure(EntityTypeBuilder<TemplatePricing> builder)
    {
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();

        // One active price per template + package.
        builder.HasIndex(x => new { x.TemplateId, x.PackageId }).IsUnique().HasFilter("is_active = true");

        builder.HasOne(x => x.Template).WithMany(t => t.Pricing).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_template_pricing_price", "price >= 0");
            t.HasCheckConstraint("ck_template_pricing_compare_at", "compare_at_price IS NULL OR compare_at_price >= price");
            t.HasCheckConstraint("ck_template_pricing_validity", "valid_to IS NULL OR valid_from IS NULL OR valid_to > valid_from");
        });
    }
}
