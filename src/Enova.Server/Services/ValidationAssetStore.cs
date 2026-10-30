namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Serves validator XMLs, XSD/XDR, codelists and transform rules from a server-side folder.
/// Clients download and cache these assets so the executable stays light and assets can be replaced without redeploying the client.
/// </summary>
public sealed class ValidationAssetStore
{
    private readonly string _root;
    private readonly ILogger<ValidationAssetStore> _logger;

    public ValidationAssetStore(IConfiguration configuration, ILogger<ValidationAssetStore> logger)
    {
        _logger = logger;
        _root = configuration["ValidationAssets:Root"]
                ?? Path.Combine(AppContext.BaseDirectory, "validation-assets");

        Directory.CreateDirectory(_root);
        _logger.LogInformation("Validation asset root: {Root}", _root);
    }

    public string Root => _root;

    /// <summary>
    /// Lists all available assets (relative paths).
    /// </summary>
    public IReadOnlyList<AssetInfo> List()
    {
        if (!Directory.Exists(_root))
            return Array.Empty<AssetInfo>();

        return Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(full =>
            {
                var relative = Path.GetRelativePath(_root, full).Replace('\\', '/');
                var fi = new FileInfo(full);
                return new AssetInfo(
                    relative,
                    fi.Length,
                    fi.LastWriteTimeUtc,
                    ComputeSha256(full));
            })
            .OrderBy(a => a.RelativePath)
            .ToList();
    }

    public AssetContent? Get(string relativePath)
    {
        var safe = Sanitize(relativePath);
        if (safe is null)
            return null;

        var full = Path.Combine(_root, safe);
        if (!File.Exists(full))
            return null;

        var bytes = File.ReadAllBytes(full);
        return new AssetContent(safe, bytes, ComputeSha256(full), File.GetLastWriteTimeUtc(full));
    }

    private static string? Sanitize(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Prevent path traversal
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
            return null;

        return normalized;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record AssetInfo(string RelativePath, long SizeBytes, DateTime LastWriteUtc, string Sha256);
public sealed record AssetContent(string RelativePath, byte[] Data, string Sha256, DateTime LastWriteUtc);

