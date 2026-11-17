using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace InviteCraft.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, versioned format) without the rest of Identity.
/// Old-format hashes report SuccessRehashNeeded so they are upgraded on next sign-in.
/// </summary>
internal sealed class PasswordHasherAdapter : Application.Common.Interfaces.IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public PasswordCheck Verify(User user, string passwordHash, string providedPassword)
    {
        try
        {
            return _inner.VerifyHashedPassword(user, passwordHash, providedPassword) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed; // corrupted/foreign hash format
        }
    }
}

/// <summary>256-bit random tokens, URL-safe; stored as SHA-256 hex.</summary>
internal sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public (string Token, string Hash) Generate()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return (token, Hash(token));
    }

    public string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
