using System.Xml.Linq;
using Lragrz.Enova.Server.Data;

namespace Lragrz.Enova.Server.Services;

public sealed class ReferenceDataService
{
    private readonly string _seedRoot;

    public ReferenceDataService(IWebHostEnvironment env)
    {
        // Prefer ContentRoot, fall back to BaseDirectory (important for single-file publish)
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "Seed"),
            Path.Combine(env.ContentRootPath, "Data", "Seed"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "Seed")
        };
        _seedRoot = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    public List<CaseworkerDto> GetCaseworkers()
    {
        var path = Path.Combine(_seedRoot, "caseworkers.xml");
        if (!File.Exists(path)) return new();

        var doc = XDocument.Load(path);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var tns = XNamespace.Get("http://www.xjustiz.de");

        return doc.Descendants("entry")
            .Select(e =>
            {
                var name = e.Attribute("name")?.Value ?? "";
                var person = e.Element(tns + "auswahl_beteiligter")?.Element(tns + "natuerlichePerson");
                var vor = person?.Element(tns + "vollerName")?.Element(tns + "vorname")?.Value ?? "";
                var nach = person?.Element(tns + "vollerName")?.Element(tns + "nachname")?.Value ?? "";
                return new CaseworkerDto(name, $"{vor} {nach}".Trim(), vor, nach);
            })
            .Where(c => !string.IsNullOrEmpty(c.Key))
            .ToList();
    }

    public Dictionary<string, List<string>> GetDecisionMappings()
    {
        var path = Path.Combine(_seedRoot, "decision-config.xml");
        if (!File.Exists(path)) return new();

        var doc = XDocument.Load(path);
        return doc.Descendants("request")
            .ToDictionary(
                r => r.Attribute("code")?.Value ?? "",
                r => r.Elements("decision").Select(d => d.Attribute("code")?.Value ?? "").Where(c => c != "").ToList()
            );
    }

    public List<CodeListEntryDto> GetCodeList(string listId)
    {
        // Serve from Schemas/codelists – simple extraction of code/value pairs
        var file = listId switch
        {
            "sachentscheidung" => "ENOVA.Sachentscheidung-1.1.xml",
            "ersuchenSachentscheidung" => "ENOVA.ErsuchenSachentscheidung-1.1.xml",
            "rollenbezeichnung" => "GDS.Rollenbezeichnung-3.5.xml",
            _ => null
        };
        if (file is null) return new();

        // Prefer embedded resource (ships inside the exe)
        var asm = typeof(ReferenceDataService).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                // Extract once so the file exists on disk for inspection
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "eNoVa_tor", "codelists");
                Directory.CreateDirectory(dir);
                var outPath = Path.Combine(dir, file);
                if (!File.Exists(outPath))
                {
                    using var fs = File.Create(outPath);
                    stream.CopyTo(fs);
                    stream.Position = 0;
                }
                return ParseCodeList(XDocument.Load(stream));
            }
        }

        // Fallback: file next to exe or in source tree
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Schemas", "codelists", file),
            Path.Combine(Directory.GetCurrentDirectory(), "Schemas", "codelists", file),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Enova.Server", "Schemas", "codelists", file)
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return new();
        return ParseCodeList(XDocument.Load(path));
    }

    private static List<CodeListEntryDto> ParseCodeList(XDocument doc)
    {
        var entries = new List<CodeListEntryDto>();
        foreach (var row in doc.Descendants().Where(e => e.Name.LocalName == "Row"))
        {
            var values = row.Descendants().Where(e => e.Name.LocalName == "SimpleValue").Select(e => e.Value).ToList();
            if (values.Count >= 2)
                entries.Add(new CodeListEntryDto(values[0], values[1]));
            else if (values.Count == 1)
                entries.Add(new CodeListEntryDto(values[0], values[0]));
        }
        return entries;
    }
}

public sealed record CaseworkerDto(string Key, string DisplayName, string Vorname, string Nachname);
public sealed record CodeListEntryDto(string Code, string Label);
