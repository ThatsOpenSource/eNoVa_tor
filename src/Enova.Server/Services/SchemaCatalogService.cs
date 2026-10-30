using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lragrz.Enova.Server.Services;

public sealed class SchemaCatalogService
{
    private readonly string _schemaRoot;
    private readonly ILogger<SchemaCatalogService> _logger;
    private SchemaManifest? _cachedManifest;
    private DateTime _manifestLoadedAt = DateTime.MinValue;

    public SchemaCatalogService(IWebHostEnvironment env, ILogger<SchemaCatalogService> logger)
    {
        _logger = logger;
        _schemaRoot = Path.Combine(env.ContentRootPath, "Schemas");
        Directory.CreateDirectory(_schemaRoot);
    }

    public SchemaManifest GetManifest(bool forceReload = false)
    {
        if (!forceReload && _cachedManifest is not null &&
            DateTime.UtcNow - _manifestLoadedAt < TimeSpan.FromMinutes(1))
        {
            return _cachedManifest;
        }

        var manifestPath = Path.Combine(_schemaRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("Schema manifest not found at {Path}", manifestPath);
            return new SchemaManifest
            {
                FormatId = "xjustiz.enova.2900003",
                SchemaVersion = "0.0.0",
                PublishedAt = DateTime.UtcNow,
                Files = Array.Empty<SchemaFileEntry>()
            };
        }

        var json = File.ReadAllText(manifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<SchemaManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new SchemaManifest();

        // Recompute hashes for integrity
        foreach (var file in manifest.Files)
        {
            var full = Path.Combine(_schemaRoot, file.RelativePath);
            if (File.Exists(full))
            {
                file.Sha256 = ComputeSha256(full);
                file.SizeBytes = new FileInfo(full).Length;
            }
        }

        _cachedManifest = manifest;
        _manifestLoadedAt = DateTime.UtcNow;
        return manifest;
    }

    public Stream? OpenFile(string relativePath)
    {
        // Prevent path traversal
        var safe = relativePath.Replace('\\', '/').TrimStart('/');
        if (safe.Contains("..", StringComparison.Ordinal))
            return null;

        var full = Path.GetFullPath(Path.Combine(_schemaRoot, safe));
        if (!full.StartsWith(Path.GetFullPath(_schemaRoot), StringComparison.OrdinalIgnoreCase))
            return null;

        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    private static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class SchemaManifest
{
    public string FormatId { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public SchemaFileEntry[] Files { get; set; } = Array.Empty<SchemaFileEntry>();
}

public sealed class SchemaFileEntry
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
}

