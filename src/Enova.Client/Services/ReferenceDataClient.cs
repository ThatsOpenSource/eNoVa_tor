using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client.Services;

public sealed class ReferenceDataClient
{
    private readonly HttpClient _http;
    private readonly EnovaConfiguration _config;

    public ReferenceDataClient(EnovaConfiguration config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? EnovaHttp.Create(TimeSpan.FromSeconds(30));
    }

    public async Task<List<CaseworkerDto>> GetCaseworkersAsync(string token, CancellationToken ct = default)
    {
        var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/caseworkers";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<CaseworkerDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public async Task<Dictionary<string, List<string>>> GetDecisionMappingsAsync(string token, CancellationToken ct = default)
    {
        var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/decision-mappings";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
    }

    public async Task<List<CodeListEntryDto>> GetCodeListAsync(string listId, string token, CancellationToken ct = default)
    {
        var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/codelists/{listId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<CodeListEntryDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }
}

public sealed record CaseworkerDto(string Key, string DisplayName, string Vorname, string Nachname);
public sealed record CodeListEntryDto(string Code, string Label);
