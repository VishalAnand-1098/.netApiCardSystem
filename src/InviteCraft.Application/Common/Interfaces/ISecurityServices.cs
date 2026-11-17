using InviteCraft.Domain.Entities.Identity;

namespace InviteCraft.Application.Common.Interfaces;

public enum PasswordCheck
{
    Failed,
    Success,
    /// <summary>Correct, but hashed with older parameters — re-hash and save.</summary>
    SuccessRehashNeeded
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    PasswordCheck Verify(User user, string passwordHash, string providedPassword);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues short-lived JWT access tokens.</summary>
public interface IAccessTokenService
{
    AccessToken Create(User user, IReadOnlyCollection<string> roles);
}

/// <summary>
/// Generates high-entropy opaque tokens (refresh tokens, email links). Only the SHA-256 hash is stored,
/// so a database leak does not expose usable tokens.
/// </summary>
public interface ISecureTokenGenerator
{
    (string Token, string Hash) Generate();
    string Hash(string token);
}

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Records security-relevant actions. Entries are saved with the caller's unit of work.</summary>
public interface IAuditLogger
{
    void Log(string action, string entityType, string? entityId = null, Guid? userId = null, object? details = null);
}
