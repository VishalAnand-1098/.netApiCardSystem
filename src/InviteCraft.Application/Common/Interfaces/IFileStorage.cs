namespace InviteCraft.Application.Common.Interfaces;

/// <summary>
/// Object storage. Keys are forward-slash paths. Keys under <c>public/</c> are served to browsers
/// (web-resolution images, fonts); everything else is private and only read by the server
/// (original artwork, generated print files). Implementations: local disk (VPS) now, S3/R2 later.
/// </summary>
public interface IFileStorage
{
    public const string PublicPrefix = "public/";
    public const string PrivatePrefix = "private/";

    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Null when the key does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Idempotent.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes every key under the prefix (e.g. all variants of one asset). Idempotent.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Browser URL for a public key; null for private keys.</summary>
    string? GetPublicUrl(string key);
}

/// <summary>An uploaded file as received by the API (stream is owned by the caller).</summary>
public sealed record FileUpload(Stream Content, string FileName, string ContentType, long Length);

public sealed record ImageInfo(string Format, string MimeType, string Extension, int Width, int Height);

/// <summary>Image inspection and resizing. Format detection uses file contents, never the name or MIME header.</summary>
public interface IImageProcessor
{
    /// <summary>Reads the header only (no full decode). Null when the data is not a JPEG, PNG or WebP image.</summary>
    ImageInfo? Inspect(Stream image);

    /// <summary>Downscales so the longest side is at most <paramref name="maxDimension"/> and encodes as WebP.</summary>
    byte[] CreateWebVariant(Stream image, int maxDimension, int quality);
}

/// <summary>Font file format detection by magic bytes.</summary>
public static class FontFiles
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static bool IsWoff2(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == (byte)'w' && header[1] == (byte)'O' && header[2] == (byte)'F' && header[3] == (byte)'2';

    public static bool IsTrueTypeOrOpenType(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && (
            (header[0] == 0x00 && header[1] == 0x01 && header[2] == 0x00 && header[3] == 0x00) ||
            (header[0] == (byte)'t' && header[1] == (byte)'r' && header[2] == (byte)'u' && header[3] == (byte)'e') ||
            (header[0] == (byte)'O' && header[1] == (byte)'T' && header[2] == (byte)'T' && header[3] == (byte)'O'));
}
