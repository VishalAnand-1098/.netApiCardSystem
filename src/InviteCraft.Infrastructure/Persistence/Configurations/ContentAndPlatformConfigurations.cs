using InviteCraft.Domain.Entities.Content;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InviteCraft.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(150);
        builder.Property(x => x.Comment).HasMaxLength(4000);
        builder.Property(x => x.AdminReply).HasMaxLength(2000);

        // One review per user per template (testimonials have no template).
        builder.HasIndex(x => new { x.UserId, x.TemplateId }).IsUnique()
            .HasFilter("template_id IS NOT NULL AND is_deleted = false");
        builder.HasIndex(x => new { x.TemplateId, x.Status });

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
    }
}

internal sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Subtitle).HasMaxLength(300);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.MobileImageUrl).HasMaxLength(1024);
        builder.Property(x => x.CtaText).HasMaxLength(60);
        builder.Property(x => x.CtaUrl).HasMaxLength(1024);

        builder.HasIndex(x => new { x.Placement, x.IsActive, x.SortOrder });

        builder.ToTable(t => t.HasCheckConstraint("ck_banners_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at"));
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(x => x.Type).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.Property(x => x.Error).HasMaxLength(2000);

        builder.HasIndex(x => new { x.UserId, x.ReadAt });
        builder.HasIndex(x => x.Status).HasFilter("status = 'Pending'");   // sender worker queue

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(x => x.Action).HasMaxLength(60).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(64);
        builder.Property(x => x.OldValues).HasColumnType("jsonb");
        builder.Property(x => x.NewValues).HasColumnType("jsonb");
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        // BRIN: tiny index for an append-only, time-ordered table.
        builder.HasIndex(x => x.CreatedAt).HasMethod("brin").HasDatabaseName("ix_audit_logs_created_at_brin");

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.Property(x => x.Value).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasData(SeedData.SiteSettings);
    }
}
