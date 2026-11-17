using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Content;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Domain.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace InviteCraft.Infrastructure.Persistence;

/// <summary>
/// EF Core context for PostgreSQL. Table/column names are snake_case (EFCore.NamingConventions) and
/// follow the DbSet names below; per-entity mapping lives in Persistence/Configurations.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Cascade at SaveChanges (not immediately on Remove) so the interceptor can turn a
        // soft-deletable principal's delete into an update before EF cascades to its children.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<FestivalCollection> FestivalCollections => Set<FestivalCollection>();
    public DbSet<FestivalCollectionTemplate> FestivalCollectionTemplates => Set<FestivalCollectionTemplate>();

    // Templates
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateAsset> TemplateAssets => Set<TemplateAsset>();
    public DbSet<TemplateEditableField> TemplateEditableFields => Set<TemplateEditableField>();
    public DbSet<Font> Fonts => Set<Font>();
    public DbSet<TemplateFontConfiguration> TemplateFontConfigurations => Set<TemplateFontConfiguration>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<TemplatePricing> TemplatePricing => Set<TemplatePricing>();

    // Invitations
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<InvitationFieldValue> InvitationFieldValues => Set<InvitationFieldValue>();
    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<SavedTemplate> SavedTemplates => Set<SavedTemplate>();

    // Commerce
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();

    // Content
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Banner> Banners => Set<Banner>();

    // Platform
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money by default; non-money decimals (mm sizes, frame ratios, ratings) override per property.
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");   // case-insensitive emails / coupon codes
        modelBuilder.HasPostgresExtension("pg_trgm");  // fuzzy template search

        // Human-friendly numbers: IC-10001…, ICO-2026-000001…
        modelBuilder.HasSequence<long>(DbSequences.TemplateCode).StartsAt(10001);
        modelBuilder.HasSequence<long>(DbSequences.OrderNumber).StartsAt(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.UseClientGeneratedKeys();
        modelBuilder.ApplySoftDeleteQueryFilters();
        modelBuilder.ApplyEnumStringConventions();
    }
}

public static class DbSequences
{
    public const string TemplateCode = "template_code_seq";
    public const string OrderNumber = "order_number_seq";
}
