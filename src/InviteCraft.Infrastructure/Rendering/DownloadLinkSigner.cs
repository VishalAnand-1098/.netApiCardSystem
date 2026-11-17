using System.Security.Cryptography;
using System.Text;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Rendering;

/// <summary>
/// HMAC-SHA256 over "downloadId.expiry" with a key derived (HKDF) from the JWT signing key, so no extra secret has to be
/// configured and a leaked download link can never be turned into an access token.
/// </summary>
internal sealed class DownloadLinkSigner : IDownloadLinkSigner
{
    private readonly byte[] key;

    public DownloadLinkSigner(IOptions<JwtSettings> jwt)
    {
        key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt.Value.SigningKey), 32,
            salt: null, info: Encoding.UTF8.GetBytes("invitecraft.download-links.v1"));
    }

    public string Sign(Guid downloadId, DateTimeOffset expiresAt) => Base64Url(Compute(downloadId, expiresAt.ToUnixTimeSeconds()));

    public bool IsValid(Guid downloadId, long expiresUnixSeconds, string signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(signature) || signature.Length > 64 || expiresUnixSeconds < now.ToUnixTimeSeconds())
            return false;

        var expected = Base64Url(Compute(downloadId, expiresUnixSeconds));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    private byte[] Compute(Guid downloadId, long expires) =>
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{downloadId:N}.{expires}"));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
