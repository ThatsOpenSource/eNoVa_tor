using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client.Services;

public sealed class AuthSession
{
    private readonly EnovaConfiguration _config;
    private readonly DocumentUploadClient _uploadClient;
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "eNoVa_tor",
        "session.bin");

    public string? Username { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Role { get; private set; }
    public string? AccessToken { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public AuthSession(EnovaConfiguration config, DocumentUploadClient uploadClient)
    {
        _config = config;
        _uploadClient = uploadClient;
    }

    public async Task LoginAsync(string username, string password, bool rememberMe, CancellationToken ct = default)
    {
        using var http = EnovaHttp.Create(TimeSpan.FromSeconds(30));
        var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/auth/login";
        var payload = JsonSerializer.Serialize(new { username, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var detail = Flatten(ex);
            var hint = detail.Contains("actively refused") || detail.Contains("No connection")
                ? "\n\nHinweis: Der Enova.Server muss zuerst gestartet sein (dotnet run --project src/Enova.Server)."
                : "";
            throw new InvalidOperationException(
                $"Verbindung zum Server fehlgeschlagen ({url}).\n{detail}{hint}", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var msg = $"Anmeldung abgelehnt (HTTP {(int)response.StatusCode}).";
            try
            {
                using var errDoc = JsonDocument.Parse(body);
                if (errDoc.RootElement.TryGetProperty("error", out var err))
                    msg = err.GetString() ?? msg;
            }
            catch { /* use default */ }
            throw new InvalidOperationException(msg);
        }

        using var doc = JsonDocument.Parse(body);
        AccessToken = doc.RootElement.GetProperty("token").GetString()
                      ?? throw new InvalidOperationException("Antwort enthält kein Token.");
        Role = doc.RootElement.GetProperty("role").GetString();
        DisplayName = doc.RootElement.TryGetProperty("displayName", out var dn)
            ? dn.GetString()
            : username;
        Username = username;

        // Keep upload client authenticated with same credentials
        await _uploadClient.LoginAsync(username, password, ct).ConfigureAwait(false);

        if (rememberMe)
            SaveEncrypted(username, AccessToken!, DisplayName ?? username, Role ?? "editor");
        else
            ClearSaved();
    }

    public bool TryRestore()
    {
        try
        {
            if (!File.Exists(StorePath))
                return false;

            var protectedBytes = File.ReadAllBytes(StorePath);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            using var doc = JsonDocument.Parse(json);

            Username = doc.RootElement.GetProperty("username").GetString();
            AccessToken = doc.RootElement.GetProperty("token").GetString();
            DisplayName = doc.RootElement.GetProperty("displayName").GetString();
            Role = doc.RootElement.GetProperty("role").GetString();
            return !string.IsNullOrEmpty(AccessToken);
        }
        catch
        {
            ClearSaved();
            return false;
        }
    }

    /// <summary>
    /// Fetches server-controlled settings (crypto, authority, sender, upload) and applies them
    /// to the local configuration object. Call after a successful login.
    /// </summary>
    public async Task ApplyRemoteConfigAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(AccessToken)) return;

        using var http = EnovaHttp.Create(TimeSpan.FromSeconds(15));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/config");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return;

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("cryptoProvider", out var cp))
            _config.CryptoProvider = cp.GetString() ?? _config.CryptoProvider;
        if (root.TryGetProperty("vdaRestEndpoint", out var vda))
            _config.VdaRestEndpoint = vda.GetString() ?? _config.VdaRestEndpoint;
        if (root.TryGetProperty("authorityIdentifier", out var auth))
            _config.AuthorityIdentifier = auth.GetString() ?? _config.AuthorityIdentifier;
        if (root.TryGetProperty("senderName", out var sender))
            _config.SenderName = sender.GetString() ?? _config.SenderName;
        if (root.TryGetProperty("uploadEnabled", out var up))
            _config.UploadEnabled = up.GetBoolean();
        if (root.TryGetProperty("localOutputDirectory", out var lod) && lod.ValueKind == JsonValueKind.String)
            _config.LocalOutputDirectory = Environment.ExpandEnvironmentVariables(lod.GetString() ?? _config.LocalOutputDirectory);
        if (root.TryGetProperty("outputAntworten", out var oa) && oa.ValueKind == JsonValueKind.String)
            _config.OutputAntworten = oa.GetString() ?? _config.OutputAntworten;
        if (root.TryGetProperty("outputAblage", out var oab) && oab.ValueKind == JsonValueKind.String)
            _config.OutputAblage = oab.GetString() ?? _config.OutputAblage;
    }

    public void Logout()
    {
        try
        {
            if (!string.IsNullOrEmpty(AccessToken))
            {
                using var http = EnovaHttp.Create(TimeSpan.FromSeconds(5));
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/auth/logout");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
                http.Send(req);
            }
        }
        catch { /* best effort */ }

        Username = null;
        DisplayName = null;
        Role = null;
        AccessToken = null;
        ClearSaved();
    }

    private static string Flatten(Exception ex)
    {
        var sb = new StringBuilder(ex.Message);
        var inner = ex.InnerException;
        while (inner is not null)
        {
            sb.Append(" → ").Append(inner.Message);
            inner = inner.InnerException;
        }
        return sb.ToString();
    }

    private static void SaveEncrypted(string username, string token, string displayName, string role)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var json = JsonSerializer.Serialize(new { username, token, displayName, role, savedAt = DateTime.UtcNow });
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StorePath, protectedBytes);
    }

    private static void ClearSaved()
    {
        try
        {
            if (File.Exists(StorePath))
                File.Delete(StorePath);
        }
        catch { /* ignore */ }
    }
}

