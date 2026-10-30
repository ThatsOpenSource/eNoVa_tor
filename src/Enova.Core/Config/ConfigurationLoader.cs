using System.Text;

namespace Lragrz.Enova.Core.Config;

/// <summary>
/// Loads key=value style config.properties (Java-compatible) plus optional overrides.
/// </summary>
public static class ConfigurationLoader
{
    public static EnovaConfiguration Load(string? explicitPath = null, string fileName = "eNoVa_tor.client.properties")
    {
        var config = new EnovaConfiguration();

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
            candidates.Add(explicitPath);

        // Next to the running assembly
        var assemblyDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(assemblyDir, fileName));
        candidates.Add(Path.Combine(assemblyDir, "config", fileName));

        // Current working directory
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), fileName));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "config", fileName));

        string? found = candidates.FirstOrDefault(File.Exists);

        if (found is null)
        {
            // First run: write a default config next to the EXE (client or server specific)
            var defaultPath = Path.Combine(assemblyDir, fileName);
            try
            {
                var content = fileName.Contains("server", StringComparison.OrdinalIgnoreCase)
                    ? DefaultServerConfig
                    : DefaultClientConfig;
                File.WriteAllText(defaultPath, content, Encoding.UTF8);
                found = defaultPath;
            }
            catch
            {
                return config;
            }
        }

        config.ConfigFilePath = found;
        var lines = File.ReadAllLines(found, Encoding.UTF8);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var idx = line.IndexOf('=');
            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            Apply(config, key, value);
        }

        return config;
    }

    private static void Apply(EnovaConfiguration config, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "enova.crypto.provider":
                config.CryptoProvider = value;
                break;
            case "enova.vda.rest.endpoint":
                config.VdaRestEndpoint = value;
                break;
            case "enova.authority.identifier":
                config.AuthorityIdentifier = value;
                break;
            case "enova.sender.name":
                config.SenderName = value;
                break;
            case "enova.server.baseurl":
                config.ServerBaseUrl = value;
                break;
            case "enova.seed.all":
                config.SeedAll = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                break;
            case "enova.upload.enabled":
                config.UploadEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                       || value == "1";
                break;
            case "enova.upload.api.endpoint":
                config.UploadApiEndpoint = value;
                break;
            case "enova.local.output.directory":
                var expanded = Environment.ExpandEnvironmentVariables(value);
                try { config.LocalOutputDirectory = Path.GetFullPath(expanded); }
                catch { config.LocalOutputDirectory = expanded; }
                break;
            case "enova.output.antworten":
                config.OutputAntworten = value;
                break;
            case "enova.output.ablage":
                config.OutputAblage = value;
                break;
        }
    }

    private const string DefaultClientConfig = """
        # =============================================================================
        # eNoVA EGOV – Client Properties
        # Author: EGOV | Version: 0.0.1
        # Auto-generated on first run. Edit as needed.
        # =============================================================================

        # Auth / config server (only local setting the client needs)
        enova.server.baseurl=http://localhost:5101

        # Local output base (client-side paths)
        enova.local.output.directory=%USERPROFILE%\\Documents\\eNoVa_tor

        # Sub-folders
        enova.output.antworten=antworten
        enova.output.ablage=ablage
        """;

    private const string DefaultServerConfig = """
        # =============================================================================
        # eNoVA EGOV – Server Properties
        # Author: EGOV | Version: 0.0.1
        # Auto-generated on first run. Edit as needed.
        # =============================================================================

        # Crypto provider the clients must use
        enova.crypto.provider=MOCK_FALLBACK

        # Production VDA / D-Trust endpoint
        enova.vda.rest.endpoint=https://d-trust.de

        # Authority identifier
        enova.authority.identifier=DE-LRAGRZ-01

        # Absender name
        enova.sender.name=LRA-GRZ

        # Listen URL
        enova.server.baseurl=http://localhost:5101

        # Upload settings served to clients
        enova.upload.enabled=false
        enova.upload.api.endpoint=http://localhost:5101/api/documents
        """;
}
