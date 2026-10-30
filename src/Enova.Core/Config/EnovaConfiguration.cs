namespace Lragrz.Enova.Core.Config;

/// <summary>
/// Strongly-typed configuration loaded from config.properties / appsettings.
/// </summary>
public sealed class EnovaConfiguration
{
    /// <summary>
    /// Crypto provider selector: MOCK_FALLBACK | VDA_PRODUCTION
    /// </summary>
    public string CryptoProvider { get; set; } = "MOCK_FALLBACK";

    /// <summary>
    /// REST endpoint of the production VDA / D-Trust style service.
    /// </summary>
    public string VdaRestEndpoint { get; set; } = "https://d-trust.de";

    /// <summary>
    /// Authority identifier used in signatures and metadata (e.g. DE-MUSTERSTADT-08).
    /// </summary>
    public string AuthorityIdentifier { get; set; } = "DE-LRAGRZ-01";

    /// <summary>
    /// Base URL of the local intranet configuration / auth server.
    /// </summary>
    public string ServerBaseUrl { get; set; } = "http://localhost:5101";

    /// <summary>
    /// When true, generated PDF and signed XML are uploaded to the configured API endpoint after local storage.
    /// </summary>
    public bool UploadEnabled { get; set; } = false;

    /// <summary>
    /// API endpoint that receives the uploaded files (DocuWare-compatible or generic).
    /// </summary>
    public string UploadApiEndpoint { get; set; } = "http://localhost:5101/api/documents";

    /// <summary>
    /// Fixed Absender name (server-side configurable, not editable in the client).
    /// </summary>
    public string SenderName { get; set; } = "LRA-GRZ";

    /// <summary>
    /// Base directory for all local artefacts (expanded from config).
    /// </summary>
    public string LocalOutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "eNoVa_tor");

    /// <summary>
    /// Sub-folder (or absolute path) for signed answer XML files.
    /// </summary>
    public string OutputAntworten { get; set; } = "antworten";

    /// <summary>
    /// Sub-folder (or absolute path) for PDF/A-3 containers.
    /// </summary>
    public string OutputAblage { get; set; } = "ablage";

    /// <summary>
    /// When true (default), seed sample users. When false, only the Organisationsverwalter is seeded.
    /// </summary>
    public bool SeedAll { get; set; } = true;

    /// <summary>
    /// Optional path to a custom config.properties file. Empty = look next to the executable.
    /// </summary>
    public string? ConfigFilePath { get; set; }
}

