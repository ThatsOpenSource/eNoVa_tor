using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Multipart upload client against the Enova server /api/documents endpoint.
/// Requires a valid JWT obtained from /api/auth/login.
/// </summary>
public sealed class DocumentUploadClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly EnovaConfiguration _config;
    private string? _bearerToken;

    public DocumentUploadClient(EnovaConfiguration config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = httpClient ?? EnovaHttp.Create(TimeSpan.FromMinutes(2));
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_bearerToken);

    public void SetToken(string token) => _bearerToken = token;

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var baseUrl = _config.ServerBaseUrl.TrimEnd('/');
        var payload = new { username, password };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.PostAsync($"{baseUrl}/api/auth/login", content, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        _bearerToken = doc.RootElement.GetProperty("token").GetString()
                       ?? throw new InvalidOperationException("Login response missing token.");
    }

    /// <summary>
    /// Uploads one or more local files (signed XML, PDF, …) as multipart/form-data.
    /// </summary>
    public async Task<UploadResult> UploadFilesAsync(
        IEnumerable<string> localFilePaths,
        CancellationToken ct = default)
    {
        if (!_config.UploadEnabled)
            throw new InvalidOperationException("Upload is disabled in configuration (enova.upload.enabled=false).");

        if (string.IsNullOrEmpty(_bearerToken))
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");

        var endpoint = string.IsNullOrWhiteSpace(_config.UploadApiEndpoint)
            ? _config.ServerBaseUrl.TrimEnd('/') + "/api/documents"
            : _config.UploadApiEndpoint;

        using var form = new MultipartFormDataContent();
        var streams = new List<FileStream>();

        try
        {
            foreach (var path in localFilePaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("File to upload not found.", path);

                var fs = File.OpenRead(path);
                streams.Add(fs);
                var streamContent = new StreamContent(fs);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(streamContent, "files", Path.GetFileName(path));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Upload failed HTTP {(int)response.StatusCode}: {body}");

            return new UploadResult(true, body);
        }
        finally
        {
            foreach (var s in streams)
                await s.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed record UploadResult(bool Success, string ResponseBody);

