using FluentValidation;
using InviteCraft.Application.Features.Account;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Admin;
using InviteCraft.Application.Features.Dashboard;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Sharing;
using InviteCraft.Application.Features.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace InviteCraft.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and all FluentValidation validators in this assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Auth & account (Phase 3)
        services.AddScoped<AuthSessionFactory>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();

        // Categories (Phase 4)
        services.AddScoped<ICategoryCatalogService, CategoryCatalogService>();
        services.AddScoped<IAdminCategoryService, AdminCategoryService>();
        services.AddScoped<IAdminSubCategoryService, AdminSubCategoryService>();

        // Templates, assets, fonts (Phase 5)
        services.AddScoped<TemplateMapper>();
        services.AddScoped<ITemplateCatalogService, TemplateCatalogService>();
        services.AddScoped<IAdminTemplateService, AdminTemplateService>();
        services.AddScoped<ITemplateAssetService, TemplateAssetService>();
        services.AddScoped<IAdminFontService, AdminFontService>();

        // Storefront content (Phase 6)
        services.AddScoped<IContentService, ContentService>();

        // Invitations & the editor (Phase 7)
        services.AddScoped<IInvitationService, InvitationService>();

        // Customer dashboard (Phase 10)
        services.AddScoped<IDashboardService, DashboardService>();

        // Admin dashboard, reports, customers, orders, coupons, reviews, audit log (Phase 11)
        services.AddScoped<IAdminReportService, AdminReportService>();
        services.AddScoped<IAdminDirectoryService, AdminDirectoryService>();
        services.AddScoped<IAdminCouponService, AdminCouponService>();

        // Sharing & notifications (Phase 12)
        services.AddOptions<SharingSettings>();      // bound from the "Sharing" section by the Infrastructure layer
        services.AddOptions<NotificationSettings>(); // bound from the "Notifications" section by the Infrastructure layer
        services.AddScoped<ISharingService, SharingService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Checkout, payments and webhooks (Phase 9)
        services.AddOptions<CheckoutSettings>(); // bound from the "Payments" section by the Infrastructure layer
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IPaymentProcessor, PaymentProcessor>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<IOrderExpiryService, OrderExpiryService>();

        // Rendering, downloads and watermarking (Phase 8)
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<IRenderJobProcessor, RenderJobProcessor>();
        services.AddScoped<ITemplateThumbnailService, TemplateThumbnailService>();

        return services;
    }
}
