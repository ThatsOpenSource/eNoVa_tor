using System.Collections.Generic;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Simple i18n holder. All client-facing strings live here so they can be
/// overridden from a properties file or server config before build/deploy.
/// Default language is German.
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> _de = new()
    {
        ["app.title"] = "eNoVA EGOV",
        ["login.title"] = "Anmeldung",
        ["login.username"] = "Benutzername",
        ["login.password"] = "Passwort",
        ["login.remember"] = "Angemeldet bleiben",
        ["login.button"] = "Anmelden",
        ["login.cancel"] = "Beenden",
        ["login.failed"] = "Anmeldung fehlgeschlagen",
        ["login.required"] = "Bitte Benutzername und Passwort eingeben.",

        ["main.open"] = "XML öffnen",
        ["main.generate"] = "Antwort generieren und exportieren",
        ["main.logout"] = "Abmelden",
        ["main.vorgang"] = "Vorgang",
        ["main.aktenzeichen"] = "Aktenzeichen",
        ["main.sachbearbeiter"] = "Sachbearbeiter",
        ["main.entscheidung"] = "Entscheidung",
        ["main.signatur"] = "Signatur",
        ["main.datei"] = "Datei",
        ["main.keineDatei"] = "Keine Datei geladen",
        ["main.bereit"] = "Bereit",
        ["main.angemeldet"] = "Angemeldet.",

        ["role.manager"] = "Organisationsverwalter",
        ["role.admin"] = "Administrator",
        ["role.editor"] = "Sachbearbeiter",
        ["role.viewer"] = "Betrachter",

        ["status.generating"] = "Antwort wird erzeugt und exportiert …",
        ["status.saved"] = "Antwort und PDF abgelegt.",
        ["status.cancelled"] = "Export abgebrochen.",
        ["status.loadFailed"] = "Laden fehlgeschlagen",
        ["status.generateFailed"] = "Export fehlgeschlagen",

        ["msg.openFirst"] = "Bitte zuerst eine XML-Datei öffnen.",
        ["msg.noDocument"] = "Kein Dokument vorhanden.",
        ["msg.hint"] = "Hinweis",
        ["msg.error"] = "Fehler",

        ["pdf.header"] = "eNoVA Entscheidung",
        ["pdf.generated"] = "Erzeugt",
        ["pdf.bearbeiter"] = "Sachbearbeiter",
        ["pdf.aktenzeichen"] = "Aktenzeichen",
        ["pdf.entscheidung"] = "Entscheidung",
        ["pdf.note"] = "Die rechtsverbindliche Fassung ist die signierte XML-Datei, die diesem PDF/A-3-Container beigefügt ist.",
    };

    private static Dictionary<string, string> _current = _de;

    public static string Get(string key) =>
        _current.TryGetValue(key, out var v) ? v : key;

    public static string Role(string? role) => role?.ToLowerInvariant() switch
    {
        "manager" => Get("role.manager"),
        "admin" => Get("role.admin"),
        "editor" => Get("role.editor"),
        "viewer" => Get("role.viewer"),
        _ => role ?? ""
    };

    /// <summary>
    /// Optional: load overrides from a key=value file (e.g. strings.de.properties).
    /// </summary>
    public static void LoadOverrides(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        foreach (var line in System.IO.File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var i = t.IndexOf('=');
            if (i <= 0) continue;
            _current[t[..i].Trim()] = t[(i + 1)..].Trim();
        }
    }
}
