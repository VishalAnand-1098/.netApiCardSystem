using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Application.Features.Auth;

public interface IAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<Result> ResendVerificationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

internal sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUserTokenRepository userTokens,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    AuthSessionFactory sessions,
    IEmailSender emailSender,
    IAuditLogger audit,
    TimeProvider timeProvider,
    IOptions<AuthSettings> options,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AuthSettings _settings = options.Value;

    // Verified against when the email is unknown, so response time does not reveal which emails exist.
    private static string? _dummyHash;
    private string DummyHash => _dummyHash ??= passwordHasher.Hash(new User(), Guid.NewGuid().ToString());

    public async Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = AuthMappings.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
            return AuthErrors.EmailTaken;

        var customerRole = await users.GetRoleAsync(RoleNames.Customer, cancellationToken)
            ?? throw new InvalidOperationException("The Customer role is missing. Apply the database migrations.");

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Phone = AuthMappings.NormalizePhone(request.Phone),
        };
        user.PasswordHash = passwordHasher.Hash(user, request.Password);
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = customerRole.Id, Role = customerRole, AssignedAt = now });
        await users.AddAsync(user, cancellationToken);

        var verificationLink = await CreateUserTokenAsync(user, TokenPurpose.EmailVerification, cancellationToken);
        var (session, _) = await sessions.CreateAsync(user, cancellationToken: cancellationToken);
        audit.Log("Register", nameof(User), user.Id.ToString(), user.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendSafelyAsync(AuthEmails.Verification(user.Email, user.FullName, verificationLink, _settings.EmailVerificationTokenHours), cancellationToken);
        return session;
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = AuthMappings.NormalizeEmail(request.Email);
        var user = await users.GetByEmailWithRolesAsync(email, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (user is null)
        {
            passwordHasher.Verify(new User(), DummyHash, request.Password);
            audit.Log("LoginFailed", nameof(User), details: new { reason = "UnknownEmail" });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
            return AuthErrors.LockedOut(MinutesUntil(lockoutEnd, now));

        var check = passwordHasher.Verify(user, user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _settings.MaxFailedLoginAttempts)
            {
                user.LockoutEnd = now.AddMinutes(_settings.LockoutMinutes);
                user.FailedLoginCount = 0;
                audit.Log("LockedOut", nameof(User), user.Id.ToString(), user.Id);
            }
            audit.Log("LoginFailed", nameof(User), user.Id.ToString(), user.Id, new { reason = "WrongPassword" });
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return user.LockoutEnd > now
                ? AuthErrors.LockedOut(_settings.LockoutMinutes)
                : AuthErrors.InvalidCredentials;
        }

        // Correct password: only now reveal that the account is disabled.
        if (!user.IsActive)
            return AuthErrors.AccountDisabled;

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.Hash(user, request.Password);

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;

        var (session, _) = await sessions.CreateAsync(user, cancellationToken: cancellationToken);
        audit.Log("Login", nameof(User), user.Id.ToString(), user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return AuthErrors.InvalidRefreshToken;

        var stored = await refreshTokens.GetByHashWithUserAsync(tokenGenerator.Hash(refreshToken), cancellationToken);
        if (stored is null)
            return AuthErrors.InvalidRefreshToken;

        var now = timeProvider.GetUtcNow();

        if (stored.RevokedAt is { } revokedAt)
        {
            // A rotated token used again outside the grace window means it was copied: kill the whole family.
            var isReuse = stored.ReplacedByTokenId is not null
                          && now - revokedAt > TimeSpan.FromSeconds(_settings.RefreshReuseGraceSeconds);
            if (isReuse)
            {
                foreach (var token in await refreshTokens.GetActiveByFamilyAsync(stored.FamilyId, now, cancellationToken))
                {
                    token.RevokedAt = now;
                    token.RevokedReason = "ReuseDetected";
                }
                audit.Log("RefreshTokenReuse", nameof(RefreshToken), stored.Id.ToString(), stored.UserId);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; token family {FamilyId} revoked", stored.UserId, stored.FamilyId);
            }
            return AuthErrors.InvalidRefreshToken;
        }

        var user = stored.User;
        if (stored.ExpiresAt <= now || !user.IsActive || user.LockoutEnd > now)
            return AuthErrors.InvalidRefreshToken;

        var (session, replacement) = await sessions.CreateAsync(user, stored.FamilyId, cancellationToken);
        stored.RevokedAt = now;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenId = replacement.Id;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Result.Success();

        var stored = await refreshTokens.GetByHashWithUserAsync(tokenGenerator.Hash(refreshToken), cancellationToken);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = timeProvider.GetUtcNow();
            stored.RevokedReason = "Logout";
            audit.Log("Logout", nameof(User), stored.UserId.ToString(), stored.UserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var token = await userTokens.GetByHashAsync(tokenGenerator.Hash(request.Token), TokenPurpose.EmailVerification, cancellationToken);
        if (token is null || !token.IsUsable(now))
            return AuthErrors.InvalidOrExpiredLink;

        token.ConsumedAt = now;
        token.User.EmailVerifiedAt ??= now;
        audit.Log("EmailVerified", nameof(User), token.UserId.ToString(), token.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ResendVerificationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdWithRolesAsync(userId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;
        if (user.EmailVerifiedAt is not null)
            return AuthErrors.AlreadyVerified;

        var link = await CreateUserTokenAsync(user, TokenPurpose.EmailVerification, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendSafelyAsync(AuthEmails.Verification(user.Email, user.FullName, link, _settings.EmailVerificationTokenHours), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        // Always succeeds: the response must not reveal whether an account exists.
        var user = await users.GetByEmailWithRolesAsync(AuthMappings.NormalizeEmail(request.Email), cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Success();

        var link = await CreateUserTokenAsync(user, TokenPurpose.PasswordReset, cancellationToken);
        audit.Log("PasswordResetRequested", nameof(User), user.Id.ToString(), user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendSafelyAsync(AuthEmails.PasswordReset(user.Email, user.FullName, link, _settings.PasswordResetTokenMinutes), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var token = await userTokens.GetByHashAsync(tokenGenerator.Hash(request.Token), TokenPurpose.PasswordReset, cancellationToken);
        if (token is null || !token.IsUsable(now))
            return AuthErrors.InvalidOrExpiredLink;

        var user = token.User;
        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.EmailVerifiedAt ??= now; // they proved access to the inbox
        token.ConsumedAt = now;

        await sessions.RevokeAllAsync(user.Id, "PasswordReset", cancellationToken);
        audit.Log("PasswordReset", nameof(User), user.Id.ToString(), user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Invalidates earlier unused tokens of the same purpose and creates a new one. Returns the email link.</summary>
    private async Task<string> CreateUserTokenAsync(User user, TokenPurpose purpose, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var previous in await userTokens.GetUnconsumedAsync(user.Id, purpose, cancellationToken))
            previous.ConsumedAt = now;

        var (raw, hash) = tokenGenerator.Generate();
        var lifetime = purpose == TokenPurpose.EmailVerification
            ? TimeSpan.FromHours(_settings.EmailVerificationTokenHours)
            : TimeSpan.FromMinutes(_settings.PasswordResetTokenMinutes);

        await userTokens.AddAsync(new UserToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = hash,
            ExpiresAt = now.Add(lifetime),
        }, cancellationToken);

        var path = purpose == TokenPurpose.EmailVerification ? "auth/verify-email" : "auth/reset-password";
        return $"{_settings.FrontendBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(raw)}";
    }

    /// <summary>Email failure must not undo a completed account action; it is logged and can be retried by the user.</summary>
    private async Task SendSafelyAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {Recipient}", message.Subject, message.ToAddress);
        }
    }

    private static int MinutesUntil(DateTimeOffset until, DateTimeOffset now) =>
        Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
}
