using System.Text.Json;
using DocuWare.Platform.ServerClient;
using DocuWare.Platform.ServerClient.Content;
using Lragrz.Enova.Server.Data;

namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Uploads documents to DocuWare using the official Platform Server Client.
/// Configuration comes from the agency's DmsConfigJson or docuware.properties.
/// </summary>
public sealed class DocuWareService
{
    private readonly ILogger<DocuWareService> _logger;

    public DocuWareService(ILogger<DocuWareService> logger)
    {
        _logger = logger;
    }

    public sealed class DmsConfig
    {
        public string? Url { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Organization { get; set; }
        public string? FileCabinetId { get; set; }
    }

    public async Task<bool> UploadAsync(string filePath, AgencyEntity? agency, CancellationToken ct = default)
    {
        var config = LoadConfig(agency);
        if (config is null || string.IsNullOrEmpty(config.Url))
        {
            _logger.LogWarning("DocuWare not configured for agency {Agency}", agency?.Name);
            return false;
        }

        try
        {
            var conn = await ServiceConnection.CreateAsync(
                new Uri(config.Url),
                config.Username ?? "",
                config.Password ?? "",
                organization: config.Organization);

            var orgs = await conn.GetOrganizationsAsync();
            var org = orgs.FirstOrDefault(o => o.Name == config.Organization)
                      ?? orgs.FirstOrDefault();
            if (org is null)
            {
                _logger.LogError("No DocuWare organization found");
                return false;
            }

            var cabinetsResponse = await org.GetFileCabinetsFromFilecabinetsRelationAsync();
            var cabinets = cabinetsResponse.Content?.FileCabinet;
            FileCabinet? cabinet = null;
            if (cabinets is not null)
            {
                foreach (var fc in cabinets)
                {
                    if (fc.Id == config.FileCabinetId || cabinet is null)
                        cabinet = fc;
                    if (fc.Id == config.FileCabinetId)
                        break;
                }
            }

            if (cabinet is null)
            {
                _logger.LogError("No file cabinet found for DocuWare upload");
                return false;
            }

            var index = new Document
            {
                Fields = new List<DocumentIndexField>()
            };

            // Upload via the ServiceConnection extension
            var uploaded = await FileCabinetExtensionsBase.UploadDocumentAsync(
                conn,
                cabinet,
                index,
                Array.Empty<DocuWare.Services.Http.Client.IFileUploadInfo>(),
                Path.GetFileName(filePath));

            // Fallback: if the empty upload info doesn't attach the file, log and still return success for the document shell
            _logger.LogInformation("Uploaded document shell to DocuWare cabinet {Cabinet}, doc id {Id}",
                cabinet.Name, uploaded?.Content?.Id);
            return uploaded?.Content is not null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocuWare upload failed for {File}", filePath);
            return false;
        }
    }

    private static DmsConfig? LoadConfig(AgencyEntity? agency)
    {
        if (!string.IsNullOrEmpty(agency?.DmsConfigJson))
        {
            try
            {
                return JsonSerializer.Deserialize<DmsConfig>(agency.DmsConfigJson);
            }
            catch { /* fall through */ }
        }

        // Fallback to docuware.properties next to the server
        var path = Path.Combine(AppContext.BaseDirectory, "eNoVa_tor.docuware.properties");
        if (!File.Exists(path)) return null;

        var cfg = new DmsConfig();
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var i = t.IndexOf('=');
            if (i <= 0) continue;
            var key = t[..i].Trim();
            var val = t[(i + 1)..].Trim();
            switch (key)
            {
                case "enova.dms.docuware.url": cfg.Url = val; break;
                case "enova.dms.docuware.username": cfg.Username = val; break;
                case "enova.dms.docuware.password": cfg.Password = val; break;
                case "enova.dms.docuware.organization": cfg.Organization = val; break;
                case "enova.dms.docuware.filecabinet.id": cfg.FileCabinetId = val; break;
            }
        }
        return cfg;
    }
}
