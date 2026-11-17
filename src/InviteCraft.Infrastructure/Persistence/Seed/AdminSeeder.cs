using InviteCraft.Application.Features.Auth;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IPasswordHasher = InviteCraft.Application.Common.Interfaces.IPasswordHasher;

namespace InviteCraft.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the first administrator from configuration (Seed:Admin:Email / Password / FullName),
/// only when no user holds the Admin role yet. Credentials belong in user-secrets or environment
/// variables — never in appsettings.json. Failures are logged, never fatal to startup.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAdminAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var config = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));

        var email = config["Seed:Admin:Email"];
        var password = config["Seed:Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogDebug("Admin seeding skipped: Seed:Admin:Email / Seed:Admin:Password not configured");
            return;
        }

        try
        {
            var db = provider.GetRequiredService<AppDbContext>();
            if (await db.UserRoles.AnyAsync(ur => ur.RoleId == SeedData.AdminRoleId, cancellationToken))
                return; // an admin already exists

            var normalized = AuthMappings.NormalizeEmail(email);
            var hasher = provider.GetRequiredService<IPasswordHasher>();
            var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
            if (user is null)
            {
                user = new User
                {
                    Email = normalized,
                    FullName = config["Seed:Admin:FullName"] ?? "InviteCraft Admin",
                    EmailVerifiedAt = now,
                };
                user.PasswordHash = hasher.Hash(user, password);
                db.Users.Add(user);
            }

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = SeedData.AdminRoleId, AssignedAt = now });
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Seeded administrator account {Email}", normalized);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Admin seeding failed; the API will start without it");
        }
    }
}
