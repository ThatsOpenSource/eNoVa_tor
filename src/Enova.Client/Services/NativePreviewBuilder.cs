using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Builds a native WPF preview (no WebView2) mirroring the original letter layout.
/// </summary>
public static class NativePreviewBuilder
{
    private const string Ns = "http://www.xjustiz.de";

    public static UIElement Build(XmlDocument document, string? caseworkerDisplayName = null, string? selectedDecision = null, string? fileNumberOverride = null)
    {
        var nsmgr = new XmlNamespaceManager(document.NameTable);
        nsmgr.AddNamespace("tns", Ns);

        string X(string xpath) =>
            document.SelectSingleNode(xpath, nsmgr)?.InnerText?.Trim() ?? "";

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };

        panel.Children.Add(new TextBlock
        {
            Text = "Internes Eingangsdokument generiert aus der XJustiz-Nachricht",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Foreground = Brushes.DimGray
        });

        // Notar block
        var refRole = X("//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_verweisGrunddaten/tns:ref.rollennummer");
        if (!string.IsNullOrEmpty(refRole))
        {
            var beteiligung = document.SelectSingleNode($"//tns:beteiligung[tns:rolle/tns:rollennummer='{refRole}']", nsmgr);
            if (beteiligung is not null)
            {
                var roleCode = beteiligung.SelectSingleNode(".//tns:rollenbezeichnung/code", nsmgr)?.InnerText?.Trim();
                var roleLabel = roleCode == "208" ? "Notar" : $"Rolle {roleCode}";
                panel.Children.Add(BuildPersonBlock(beteiligung, nsmgr, roleLabel));
            }
        }

        // Nachricht
        panel.Children.Add(Separator());
        panel.Children.Add(Header("Nachricht"));
        panel.Children.Add(Row("Gegenstand", X("//tns:grunddaten/tns:verfahrensdaten/tns:instanzdaten/tns:verfahrensgegenstand/tns:gegenstand")));
        panel.Children.Add(Row("Aktenzeichen", string.IsNullOrEmpty(fileNumberOverride) ? X("//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen") : fileNumberOverride));
        panel.Children.Add(Row("Erstellt", X("//tns:nachrichtenkopf/tns:erstellungszeitpunkt")));
        panel.Children.Add(Row("Absender", X("//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_kommunikationspartner/tns:sonstige")));

        // Parties
        AppendRoleBlock(panel, document, nsmgr, "168", "Verkäufer");
        AppendRoleBlock(panel, document, nsmgr, "072", "Käufer");

        // Urkunde
        panel.Children.Add(Separator());
        panel.Children.Add(Header("Urkunde"));
        panel.Children.Add(Row("UVZ-Nr.", X("//tns:datenDerUrkunde/tns:urNr")));
        panel.Children.Add(Row("Beurkundung", X("//tns:datenDerUrkunde/tns:urkundsdatum")));

        // Grundstück
        panel.Children.Add(Header("Grundstück"));
        panel.Children.Add(Row("Amtsgericht", X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:amtsgericht")));
        panel.Children.Add(Row("Bezirk", X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:grundbuchbezirk")));
        panel.Children.Add(Row("Blatt", X("//tns:beschreibungDesBetroffenenRechts/tns:bundeslandfremdesGrundbuchblatt/tns:grundbuchblatt")));

        var flurstuecke = document.SelectNodes("//tns:flurstuecke/tns:identifikationFlurstueck", nsmgr);
        if (flurstuecke is not null)
        {
            foreach (XmlNode fs in flurstuecke)
            {
                var gem = fs.SelectSingleNode("tns:gemarkungsschluessel", nsmgr)?.InnerText?.Trim() ?? "";
                var z = fs.SelectSingleNode("tns:flurstuecksnummer/tns:zaehler", nsmgr)?.InnerText?.Trim() ?? "";
                var n = fs.SelectSingleNode("tns:flurstuecksnummer/tns:nenner", nsmgr)?.InnerText?.Trim() ?? "";
                var nr = string.IsNullOrEmpty(n) ? z : $"{z}/{n}";
                panel.Children.Add(Row("Flurstück", $"{gem} · {nr}"));
            }
        }

        // Entscheidung / Ersuchen
        var sach = document.SelectNodes("//tns:sachentscheidung/tns:sachentscheidung/code", nsmgr);
        var ersuchen = document.SelectNodes("//tns:ersuchenSachentscheidung/tns:ersuchenSachentscheidung/code", nsmgr);
        if (sach is { Count: > 0 })
        {
            panel.Children.Add(Separator());
            panel.Children.Add(Header("Entscheidung"));
            foreach (XmlNode c in sach)
                panel.Children.Add(Row("Code", c.InnerText?.Trim() ?? ""));
        }
        else if (ersuchen is { Count: > 0 })
        {
            panel.Children.Add(Header("Ersuchen"));
            foreach (XmlNode c in ersuchen)
                panel.Children.Add(Row("Code", c.InnerText?.Trim() ?? ""));
        }

        if (!string.IsNullOrEmpty(selectedDecision))
        {
            panel.Children.Add(Separator());
            panel.Children.Add(Header("Sachbearbeiter-Entscheidung"));
            panel.Children.Add(Row("Entscheidung", selectedDecision));
        }

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White
        };
    }

    private static void AppendRoleBlock(StackPanel panel, XmlDocument doc, XmlNamespaceManager nsmgr, string code, string label)
    {
        var nodes = doc.SelectNodes($"//tns:beteiligung[tns:rolle/tns:rollenbezeichnung/code='{code}']", nsmgr);
        if (nodes is null || nodes.Count == 0) return;
        panel.Children.Add(Header(label));
        foreach (XmlNode n in nodes)
            panel.Children.Add(BuildPersonBlock(n, nsmgr, label));
    }

    private static UIElement BuildPersonBlock(XmlNode beteiligung, XmlNamespaceManager nsmgr, string roleLabel)
    {
        var box = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
        var vor = beteiligung.SelectSingleNode(".//tns:vollerName/tns:vorname", nsmgr)?.InnerText?.Trim() ?? "";
        var nach = beteiligung.SelectSingleNode(".//tns:vollerName/tns:nachname", nsmgr)?.InnerText?.Trim() ?? "";
        var str = beteiligung.SelectSingleNode(".//tns:anschrift/tns:strasse", nsmgr)?.InnerText?.Trim() ?? "";
        var nr = beteiligung.SelectSingleNode(".//tns:anschrift/tns:hausnummer", nsmgr)?.InnerText?.Trim() ?? "";
        var plz = beteiligung.SelectSingleNode(".//tns:anschrift/tns:postleitzahl", nsmgr)?.InnerText?.Trim() ?? "";
        var ort = beteiligung.SelectSingleNode(".//tns:anschrift/tns:ort", nsmgr)?.InnerText?.Trim() ?? "";

        box.Children.Add(new TextBlock { Text = $"{roleLabel}: {vor} {nach}".Trim(), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        if (!string.IsNullOrEmpty(str))
            box.Children.Add(new TextBlock { Text = $"{str} {nr}".Trim(), FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        if (!string.IsNullOrEmpty(plz) || !string.IsNullOrEmpty(ort))
            box.Children.Add(new TextBlock { Text = $"{plz} {ort}".Trim(), FontSize = 16 });
        return box;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 6),
        Foreground = Brushes.Black,
        Padding = new Thickness(0, 4, 0, 4)
    };

    private static Border Separator() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        Margin = new Thickness(0, 8, 0, 8)
    };

    private static UIElement Row(string label, string value)
    {
        var tb = new TextBox
        {
            Text = $"{label}:  {(string.IsNullOrEmpty(value) ? "—" : value)}",
            FontSize = 16,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 2, 0, 2),
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0)
        };
        return tb;
    }
}
