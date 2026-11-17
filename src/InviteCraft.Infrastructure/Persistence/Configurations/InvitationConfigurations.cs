using InviteCraft.Domain.Entities.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InviteCraft.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(10).IsRequired();
        builder.Property(x => x.StyleOverrides).HasColumnType("jsonb");
        builder.Property(x => x.ShareSlug).HasMaxLength(32);
        builder.Property(x => x.PreviewImageKey).HasMaxLength(512);
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasIndex(x => x.ShareSlug).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt }).IsDescending(false, true);   // "My Invitations"
        builder.HasIndex(x => x.TemplateId);

        builder.HasOne(x => x.User).WithMany(u => u.Invitations).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UnlockedPackage).WithMany().HasForeignKey(x => x.UnlockedPackageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DuplicatedFrom).WithMany().HasForeignKey(x => x.DuplicatedFromId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_invitations_unlock", "(unlocked_package_id IS NULL) = (unlocked_at IS NULL)"));
    }
}

internal sealed class InvitationFieldValueConfiguration : IEntityTypeConfiguration<InvitationFieldValue>
{
    public void Configure(EntityTypeBuilder<InvitationFieldValue> builder)
    {
        builder.Property(x => x.StyleOverride).HasColumnType("jsonb");
        builder.Property(x => x.FrameOverride).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.InvitationId, x.FieldId }).IsUnique();

        builder.HasOne(x => x.Invitation).WithMany(i => i.FieldValues).HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Cascade);
        // Fields that customers have filled in cannot be hard-deleted; admins deactivate them instead.
        builder.HasOne(x => x.Field).WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DownloadConfiguration : IEntityTypeConfiguration<Download>
{
    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.Property(x => x.StorageKey).HasMaxLength(512);
        builder.Property(x => x.Error).HasMaxLength(2000);
        builder.Property(x => x.UserAgent).HasMaxLength(512);

        builder.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => x.Status).HasFilter("status IN ('Queued', 'Rendering')");   // render worker queue

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Invitation).WithMany(i => i.Downloads).HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t => t.HasCheckConstraint("ck_downloads_dpi", "dpi BETWEEN 72 AND 1200"));
    }
}

internal sealed class SavedTemplateConfiguration : IEntityTypeConfiguration<SavedTemplate>
{
    public void Configure(EntityTypeBuilder<SavedTemplate> builder)
    {
        builder.HasKey(x => new { x.UserId, x.TemplateId });

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}
