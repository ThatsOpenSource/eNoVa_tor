using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Downloads validator XSD / codelist artefacts from the intranet server once,
/// caches them under LocalApplicationData\EnovaEgov\Schemas, and only contacts
/// the server for a lightweight heartbeat (manifest version / hash check).
/// </summary>
public sealed class SchemaCacheService
{
    private readonly EnovaConfiguration _config;
    private readonly HttpClient _http;
    private readonly string _cacheRoot;

    public SchemaCacheService(EnovaConfiguration config, HttpClient? httpClient = null)
    {
        _config = config;
        _http = httpClient ?? EnovaHttp.Create(TimeSpan.FromSeconds(30));
        _cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eNoVa_tor",
            "Schemas");
        Directory.CreateDirectory(_cacheRoot);
    }

    public string CacheRoot => _cacheRoot;

    /// <summary>
    /// Ensures schemas for the given format are present and up-to-date.
    /// Returns the local directory that contains the validator files.
    /// </summary>
    public async Task<SchemaSyncResult> EnsureSchemasAsync(
        string formatId = "xjustiz.enova.2900003",
        CancellationToken ct = default)
    {
        var localManifestPath = Path.Combine(_cacheRoot, "manifest.json");
        SchemaManifestDto? local = null;

        if (File.Exists(localManifestPath))
        {
            try
            {
                local = JsonSerializer.Deserialize<SchemaManifestDto>(
                    await File.ReadAllTextAsync(localManifestPath, ct).ConfigureAwait(false),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // corrupt cache – force re-download
            }
        }

        SchemaManifestDto? remote = null;
        try
        {
            var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/schemas/manifest";
            remote = await _http.GetFromJsonAsync<SchemaManifestDto>(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (local is not null && local.FormatId == formatId)
            {
                return new SchemaSyncResult(true, false, local.SchemaVersion, _cacheRoot,
                    $"Offline – using cached schema {local.SchemaVersion}. ({ex.Message})");
            }

            return new SchemaSyncResult(false, false, null, _cacheRoot,
                $"Cannot reach schema server and no local cache: {ex.Message}");
        }

        if (remote is null)
            return new SchemaSyncResult(false, false, null, _cacheRoot, "Empty remote manifest.");

        var needsDownload = local is null
                            || local.SchemaVersion != remote.SchemaVersion
                            || local.FormatId != remote.FormatId
                            || !AllFilesPresentAndValid(remote);

        if (!needsDownload)
        {
            return new SchemaSyncResult(true, false, remote.SchemaVersion, _cacheRoot,
                $"Schemas up-to-date (v{remote.SchemaVersion}).");
        }

        foreach (var file in remote.Files)
        {
            var downloadUrl = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/schemas/files/{file.RelativePath}";
            var targetPath = Path.Combine(_cacheRoot, file.RelativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            await using var remoteStream = await _http.GetStreamAsync(downloadUrl, ct).ConfigureAwait(false);
            await using var fs = File.Create(targetPath);
            await remoteStream.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(
            localManifestPath,
            JsonSerializer.Serialize(remote, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        return new SchemaSyncResult(true, true, remote.SchemaVersion, _cacheRoot,
            $"Schemas updated to v{remote.SchemaVersion} ({remote.Files.Length} files).");
    }

    public string? ResolveLocalSchemaPath(string fileName)
    {
        var path = Path.Combine(_cacheRoot, fileName);
        return File.Exists(path) ? path : null;
    }

    private bool AllFilesPresentAndValid(SchemaManifestDto remote)
    {
        foreach (var file in remote.Files)
        {
            var path = Path.Combine(_cacheRoot, file.RelativePath);
            if (!File.Exists(path))
                return false;

            if (!string.IsNullOrEmpty(file.Sha256) &&
                !file.Sha256.StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
            {
                var hash = ComputeSha256(path);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}

public sealed record SchemaSyncResult(
    bool Success,
    bool Downloaded,
    string? Version,
    string CachePath,
    string Message);

public sealed class SchemaManifestDto
{
    public string FormatId { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public SchemaFileEntryDto[] Files { get; set; } = Array.Empty<SchemaFileEntryDto>();
}

public sealed class SchemaFileEntryDto
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
}

