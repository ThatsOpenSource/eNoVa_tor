using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Lragrz.Enova.Core.Crypto;

/// <summary>
/// Production QES provider. Before any call to the external VDA endpoint it
/// requires a short-lived sign ticket obtained from the intranet server
/// (POST /api/sign/authorize) after credential / role verification.
/// </summary>
public sealed class ProductionVdaProvider : IQesProvider
{
    private readonly ILogger<ProductionVdaProvider> _logger;
    private readonly string _endpoint;
    private readonly string _authorityIdentifier;
    private readonly string _serverBaseUrl;
    private readonly HttpClient _httpClient;
    private string? _signTicket;
    private string? _loginToken;

    public string ProviderId => "VDA_PRODUCTION";
    public string DisplayName => "Production VDA (REST)";

    public ProductionVdaProvider(
        ILogger<ProductionVdaProvider> logger,
        string endpoint,
        string authorityIdentifier,
        string? serverBaseUrl = null,
        HttpClient? httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpoint = string.IsNullOrWhiteSpace(endpoint)
            ? throw new ArgumentException("VDA endpoint must not be empty.", nameof(endpoint))
            : endpoint.TrimEnd('/');
        _authorityIdentifier = authorityIdentifier ?? "DE-MUSTERSTADT-08";
        _serverBaseUrl = (serverBaseUrl ?? "https://localhost:5101").TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public void SetLoginToken(string jwt) => _loginToken = jwt;
    public void SetSignTicket(string ticket) => _signTicket = ticket;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"{_endpoint}/health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VDA health check failed for {Endpoint}", _endpoint);
            return false;
        }
    }

    public async Task<XmlDocument> SignEnvelopedAsync(
        XmlDocument document,
        string? elementToSign = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureSignTicketAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(_signTicket))
            throw new InvalidOperationException(
                "No valid sign ticket. User credentials were not verified by the intranet server.");

        _logger.LogInformation(
            "VDA_PRODUCTION: Credential check passed. Requesting remote signature from {Endpoint}",
            _endpoint);

        string payload;
        using (var sw = new StringWriter())
        {
            document.Save(sw);
            payload = sw.ToString();
        }

        var requestBody = new
        {
            authorityIdentifier = _authorityIdentifier,
            xmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
            signatureType = "enveloped",
            referenceUri = elementToSign,
            signTicket = _signTicket
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/api/v1/sign")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _signTicket);

        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"VDA signing failed with HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<VdaSignResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("VDA returned an empty response.");

        if (string.IsNullOrWhiteSpace(result.SignedXmlBase64))
            throw new InvalidOperationException("VDA response did not contain SignedXmlBase64.");

        var signedBytes = Convert.FromBase64String(result.SignedXmlBase64);
        var signedDoc = new XmlDocument { PreserveWhitespace = true };
        signedDoc.Load(new MemoryStream(signedBytes));

        _signTicket = null;

        _logger.LogInformation("VDA_PRODUCTION: Remote signature successfully received.");
        return signedDoc;
    }

    private async Task EnsureSignTicketAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_signTicket))
            return;

        if (string.IsNullOrEmpty(_loginToken))
            throw new InvalidOperationException(
                "Production signing requires a login token. Call SetLoginToken() after /api/auth/login.");

        var body = new
        {
            signPassword = (string?)null,
            authorityIdentifier = _authorityIdentifier
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_serverBaseUrl}/api/sign/authorize")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _loginToken);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException(
                "Sign authorization denied. Check user role (editor/admin) and credentials.");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        _signTicket = doc.RootElement.GetProperty("signTicket").GetString();

        if (string.IsNullOrEmpty(_signTicket))
            throw new InvalidOperationException("Server did not return a signTicket.");
    }

    private sealed class VdaSignResponse
    {
        public string? SignedXmlBase64 { get; set; }
        public string? SignatureId { get; set; }
    }
}

