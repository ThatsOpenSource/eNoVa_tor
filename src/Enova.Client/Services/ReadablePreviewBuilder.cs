using System.Text;
using System.Xml;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Builds a plain black-and-white monospace letter view from an eNoVA DOM.
/// Follows the original Input.htm sequence: Hinweis → Notar/Absender block → parties → Urkunde → Grundstück → Ersuchen.
/// </summary>
public static class ReadablePreviewBuilder
{
    private const string Ns = "http://www.xjustiz.de";

    public static string Build(XmlDocument document, string? caseworkerDisplayName = null)
    {
        var nsmgr = new XmlNamespaceManager(document.NameTable);
        nsmgr.AddNamespace("tns", Ns);

        string X(string xpath) =>
            HtmlSanitizer.Text(document.SelectSingleNode(xpath, nsmgr)?.InnerText);

        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="de"><head><meta charset="utf-8"/>
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline';"/>
<style>
  html,body { margin:0; background:#fff; color:#111; font-family:'Consolas','Courier New',monospace; font-size:15px; line-height:1.55; }
  body { padding:28px 32px; max-width:820px; }
  .hinweis { text-align:center; font-weight:600; margin:0 0 28px; color:#333; letter-spacing:0.01em; }
  h2 { font-size:15px; font-weight:600; margin:24px 0 10px; padding-bottom:4px; border-bottom:1px solid #ddd; color:#000; }
  .block { margin:12px 0 16px; }
  .person { margin:8px 0 12px; padding-left:4px; }
  .row { margin:3px 0; }
  .label { display:inline-block; min-width:150px; color:#444; }
  strong { font-weight:600; }
</style>
<script>
  document.addEventListener('contextmenu', function(e){ e.preventDefault(); });
</script>
</head><body>
<div class="hinweis">Internes Eingangsdokument generiert aus der XJustiz-Nachricht</div>
""");

        // Notar / Absender block via ref.rollennummer (original sequence)
        var refRole = X("//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_verweisGrunddaten/tns:ref.rollennummer");
        if (!string.IsNullOrEmpty(refRole))
        {
            var beteiligung = document.SelectSingleNode(
                $"//tns:beteiligung[tns:rolle/tns:rollennummer='{refRole}']", nsmgr);
            if (beteiligung is not null)
            {
                var roleCode = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:rollenbezeichnung/code", nsmgr)?.InnerText);
                var roleLabel = roleCode == "208" ? "Notar" : $"Rolle {roleCode}";
                sb.Append("<div class=\"block\">");
                AppendPersonLines(sb, beteiligung, nsmgr, roleLabel);
                sb.Append("</div>");
            }
        }

        var gegenstand = X("//tns:grunddaten/tns:verfahrensdaten/tns:instanzdaten/tns:verfahrensgegenstand/tns:gegenstand");
        var aktenzeichen = X("//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen");
        var erstellt = FormatDate(X("//tns:nachrichtenkopf/tns:erstellungszeitpunkt"));
        var absenderName = X("//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_kommunikationspartner/tns:sonstige");

        sb.Append("<h2>Nachricht</h2>");
        sb.Append("<div class=\"row\"><span class=\"label\">Gegenstand</span>").Append(string.IsNullOrEmpty(gegenstand) ? "—" : gegenstand).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"label\">Aktenzeichen</span>").Append(string.IsNullOrEmpty(aktenzeichen) ? "—" : aktenzeichen).Append("</div>");
        if (!string.IsNullOrEmpty(erstellt))
            sb.Append("<div class=\"row\"><span class=\"label\">Erstellt</span>").Append(erstellt).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"label\">Absender</span>").Append(string.IsNullOrEmpty(absenderName) ? "—" : absenderName).Append("</div>");
        if (!string.IsNullOrEmpty(caseworkerDisplayName))
            sb.Append("<div class=\"row\"><span class=\"label\">Bearbeitet von</span>").Append(HtmlSanitizer.Text(caseworkerDisplayName)).Append("</div>");

        // Parties
        AppendRoleBlock(sb, document, nsmgr, "168", "Verkäufer");
        AppendRoleBlock(sb, document, nsmgr, "072", "Käufer");

        // Urkunde
        sb.Append("<h2>Urkunde</h2>");
        sb.Append("<div class=\"row\"><span class=\"label\">UVZ-Nr.</span>").Append(X("//tns:datenDerUrkunde/tns:urNr")).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"label\">Beurkundung</span>")
          .Append(HtmlSanitizer.Text(FormatDate(document.SelectSingleNode("//tns:datenDerUrkunde/tns:urkundsdatum", nsmgr)?.InnerText))).Append("</div>");

        // Grundstück
        sb.Append("<h2>Grundstück</h2>");
        sb.Append("<div class=\"row\"><span class=\"label\">Amtsgericht</span>")
          .Append(X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:amtsgericht")).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"label\">Bezirk</span>")
          .Append(X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:grundbuchbezirk")).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"label\">Blatt</span>")
          .Append(X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:grundbuchblatt")).Append("</div>");

        var flurstuecke = document.SelectNodes("//tns:flurstuecke/tns:identifikationFlurstueck", nsmgr);
        if (flurstuecke is not null)
        {
            foreach (XmlNode fs in flurstuecke)
            {
                var gem = HtmlSanitizer.Text(fs.SelectSingleNode("tns:gemarkungsschluessel", nsmgr)?.InnerText);
                var z = HtmlSanitizer.Text(fs.SelectSingleNode("tns:flurstuecksnummer/tns:zaehler", nsmgr)?.InnerText);
                var n = HtmlSanitizer.Text(fs.SelectSingleNode("tns:flurstuecksnummer/tns:nenner", nsmgr)?.InnerText);
                var nr = string.IsNullOrEmpty(n) ? z : $"{z}/{n}";
                sb.Append("<div class=\"row\"><span class=\"label\">Flurstück</span>").Append(gem).Append(" · ").Append(nr).Append("</div>");
            }
        }

        // Ersuchen / Entscheidung
        var ersuchen = document.SelectNodes("//tns:ersuchenSachentscheidung/tns:ersuchenSachentscheidung/code", nsmgr);
        var sach = document.SelectNodes("//tns:sachentscheidung/tns:sachentscheidung/code", nsmgr);
        if (sach is { Count: > 0 })
        {
            sb.Append("<h2>Entscheidung</h2>");
            foreach (XmlNode c in sach)
                sb.Append("<div class=\"row\"><span class=\"label\">Code</span>").Append(HtmlSanitizer.Text(c.InnerText)).Append("</div>");
        }
        else if (ersuchen is { Count: > 0 })
        {
            sb.Append("<h2>Ersuchen</h2>");
            foreach (XmlNode c in ersuchen)
                sb.Append("<div class=\"row\"><span class=\"label\">Code</span>").Append(HtmlSanitizer.Text(c.InnerText)).Append("</div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendPersonLines(StringBuilder sb, XmlNode beteiligung, XmlNamespaceManager nsmgr, string roleLabel)
    {
        var vor = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:vollerName/tns:vorname", nsmgr)?.InnerText);
        var nach = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:vollerName/tns:nachname", nsmgr)?.InnerText);
        var str = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:anschrift/tns:strasse", nsmgr)?.InnerText);
        var nr = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:anschrift/tns:hausnummer", nsmgr)?.InnerText);
        var plz = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:anschrift/tns:postleitzahl", nsmgr)?.InnerText);
        var ort = HtmlSanitizer.Text(beteiligung.SelectSingleNode(".//tns:anschrift/tns:ort", nsmgr)?.InnerText);

        sb.Append("<div class=\"person\"><strong>").Append(roleLabel).Append("</strong><br/>");
        sb.Append(vor).Append(' ').Append(nach).Append("<br/>");
        sb.Append(str).Append(' ').Append(nr).Append("<br/>");
        sb.Append(plz).Append(' ').Append(ort).Append("</div>");
    }

    private static void AppendRoleBlock(StringBuilder sb, XmlDocument document, XmlNamespaceManager nsmgr, string code, string title)
    {
        var nodes = document.SelectNodes(
            $"//tns:beteiligung[tns:rolle/tns:rollenbezeichnung/code='{code}']", nsmgr);
        if (nodes is null || nodes.Count == 0) return;

        sb.Append("<h2>").Append(HtmlSanitizer.Text(title)).Append("</h2>");
        foreach (XmlNode b in nodes)
            AppendPersonLines(sb, b, nsmgr, title);
    }

    private static string? FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        iso = iso.Trim();
        if (iso.Length >= 10 && iso[4] == '-' && iso[7] == '-')
            return $"{iso[8..10]}.{iso[5..7]}.{iso[0..4]}";
        return iso;
    }
}
