using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Admin;
using InviteCraft.Application.Features.Dashboard;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Infrastructure.Auditing;
using InviteCraft.Infrastructure.Email;
using InviteCraft.Infrastructure.Identity;
using InviteCraft.Infrastructure.Persistence;
using InviteCraft.Infrastructure.Persistence.Interceptors;
using InviteCraft.Infrastructure.Persistence.Repositories;
using InviteCraft.Infrastructure.Payments;
using InviteCraft.Infrastructure.Rendering;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Infrastructure.Storage;
using InviteCraft.Application.Features.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IPasswordHasher = InviteCraft.Application.Common.Interfaces.IPasswordHasher;

namespace InviteCraft.Infrastructure;

public static class DependencyInjection
{
    public const string ReadinessTag = "ready";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:Default' is not configured. " +
                "Set it in appsettings.Development.json, user-secrets, or the ConnectionStrings__Default environment variable.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        AddPersistence(services, connectionString);
        AddIdentityServices(services, configuration);
        AddEmail(services, configuration);
        AddStorage(services, configuration);
        AddRendering(services, configuration);
        AddPayments(services, configuration);

        // Readiness = can we reach PostgreSQL. Liveness has no dependencies (see Api pipeline).
        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgresql", tags: [ReadinessTag]);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, string connectionString)
    {
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>())
            // Required navigations to soft-deletable principals are intentional (rows are never hard-deleted).
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserTokenRepository, UserTokenRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ISubCategoryRepository, SubCategoryRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IFontRepository, FontRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<INotificationsRepository, NotificationsRepository>();
        services.AddScoped<IDownloadRepository, DownloadRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        services.AddScoped<IPersistenceConflicts, PersistenceConflicts>();
        services.AddScoped<IAuditLogger, AuditLogger>();
    }

    private static void AddIdentityServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthSettings>()
            .Bind(configuration.GetSection(AuthSettings.SectionName))
            .Validate(s => s.AccessTokenMinutes is > 0 and <= 60, "Auth:AccessTokenMinutes must be between 1 and 60.")
            .Validate(s => s.RefreshTokenDays is > 0 and <= 90, "Auth:RefreshTokenDays must be between 1 and 90.")
            .Validate(s => Uri.IsWellFormedUriString(s.FrontendBaseUrl, UriKind.Absolute), "Auth:FrontendBaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(s => System.Text.Encoding.UTF8.GetByteCount(s.SigningKey) >= JwtSettings.MinimumKeyBytes,
                $"Jwt:SigningKey must be at least {JwtSettings.MinimumKeyBytes} bytes. Set it with user-secrets or the Jwt__SigningKey environment variable.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IAccessTokenService, JwtTokenService>();
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageSettings>()
            .Bind(configuration.GetSection(StorageSettings.SectionName))
            .Validate(s => s.Provider == "Local", "Only Storage:Provider = \"Local\" is available until the S3 provider ships (Phase 13).")
            .ValidateOnStart();

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
    }

    private static void AddPayments(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PaymentSettings.SectionName);
        services.AddOptions<PaymentSettings>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaymentSettings>, PaymentSettingsValidator>();
        services.Configure<CheckoutSettings>(section);

        // Razorpay: one named HTTP client carrying the credentials; the simulated gateway needs none (and is refused outside Development).
        services.AddHttpClient(RazorpayGateway.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<PaymentSettings>>().Value;
            client.BaseAddress = new Uri(settings.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = RazorpayGateway.BasicAuth(settings);
        });
        services.AddSingleton<RazorpayGateway>();
        services.AddSingleton<SimulatedGateway>();
        services.AddSingleton<IPaymentGateway>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<PaymentSettings>>().Value.Provider, PaymentSettings.SimulatedProvider, StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<SimulatedGateway>()
                : sp.GetRequiredService<RazorpayGateway>());
        services.AddHostedService<OrderExpiryWorker>();

        // Sharing & notifications (Phase 12). Share-image rendering follows the Rendering:Enabled switch.
        services.AddOptions<Application.Features.Sharing.SharingSettings>().Configure(options =>
        {
            configuration.GetSection("Sharing").Bind(options);
            if (!configuration.GetValue("Rendering:Enabled", true))
                options.RenderImages = false;
        });
        services.AddOptions<NotificationSettings>().Bind(configuration.GetSection("Notifications"));
        services.AddHostedService<Notifications.NotificationEmailWorker>();
    }

    private static void AddRendering(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RenderSettings>()
            .Bind(configuration.GetSection(RenderSettings.SectionName))
            .Validate(s => s.MaxConcurrency is >= 1 and <= 8, "Rendering:MaxConcurrency must be between 1 and 8.")
            .Validate(s => s.TimeoutSeconds is >= 5 and <= 300, "Rendering:TimeoutSeconds must be between 5 and 300.")
            .ValidateOnStart();

        // One shared headless browser (started on first use), many isolated contexts.
        services.AddSingleton<PlaywrightCardRenderer>();
        services.AddSingleton<ICardRenderer>(sp => sp.GetRequiredService<PlaywrightCardRenderer>());
        services.AddSingleton<RenderQueue>();
        services.AddSingleton<IRenderQueue>(sp => sp.GetRequiredService<RenderQueue>());
        services.AddSingleton<IDownloadLinkSigner, DownloadLinkSigner>();
        services.AddHostedService<RenderWorker>();
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailSettings.SectionName);
        services.Configure<EmailSettings>(section);

        var mode = section.GetValue<string>(nameof(EmailSettings.Mode)) ?? "Smtp";
        if (string.Equals(mode, "Pickup", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailSender, PickupDirectoryEmailSender>();
        else
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
    }
}
