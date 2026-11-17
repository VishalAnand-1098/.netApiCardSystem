using InviteCraft.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Storage;

public sealed class StorageSettings
{
    public const string SectionName = "Storage";

    /// <summary>"Local" (VPS disk). "S3" (AWS S3 / Cloudflare R2 / MinIO) is added in Phase 13.</summary>
    public string Provider { get; init; } = "Local";

    /// <summary>Root folder for Local storage; relative paths resolve against the API content root.</summary>
    public string LocalRootPath { get; init; } = "App_Data/storage";

    /// <summary>URL prefix under which public/ keys are served (relative works behind the dev proxy and nginx).</summary>
    public string PublicBaseUrl { get; init; } = "/media";

    public string ResolveRoot(string contentRootPath) => Path.GetFullPath(Path.Combine(contentRootPath, LocalRootPath));
}

/// <summary>
/// Disk-backed storage: &lt;root&gt;/public/** is served as static files at PublicBaseUrl,
/// &lt;root&gt;/private/** is never exposed over HTTP. Keys are validated so they can never escape the root.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _publicBaseUrl;

    public LocalFileStorage(IOptions<StorageSettings> options, IHostEnvironment environment)
    {
        _root = options.Value.ResolveRoot(environment.ContentRootPath);
        _publicBaseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        Directory.CreateDirectory(Path.Combine(_root, "public"));
        Directory.CreateDirectory(Path.Combine(_root, "private"));
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file then move, so readers never see a half-written file.
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await content.CopyToAsync(file, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(PathFor(key)));

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var path = PathFor(prefix.TrimEnd('/'));
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    public string? GetPublicUrl(string key)
    {
        ValidateKey(key);
        return key.StartsWith(IFileStorage.PublicPrefix, StringComparison.Ordinal)
            ? $"{_publicBaseUrl}/{key[IFileStorage.PublicPrefix.Length..]}"
            : null;
    }

    private string PathFor(string key)
    {
        ValidateKey(key);
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Storage key escapes the storage root: {key}", nameof(key));
        return full;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Contains("..", StringComparison.Ordinal)
            || key.Contains('\\')
            || key.Contains(':')
            || key.StartsWith('/')
            || !(key.StartsWith(IFileStorage.PublicPrefix, StringComparison.Ordinal) || key.StartsWith(IFileStorage.PrivatePrefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invalid storage key: {key}", nameof(key));
        }
    }
}
