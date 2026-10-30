using System.Xml;

namespace Lragrz.Enova.Core.Xml;

/// <summary>
/// Validates that an incoming document is a proper eNoVA 2900003 Ersuchen um Sachentscheidung
/// (mirrors the original Java ErsuchenSachentscheidungChecker).
/// </summary>
public static class ErsuchenSachentscheidungChecker
{
    private const string Ns = "http://www.xjustiz.de";
    private const string ExpectedRoot = "nachricht.enova.entscheidung.2900003";

    public static void Check(XmlDocument document)
    {
        var root = document.DocumentElement
                   ?? throw new InvalidOperationException("Die ausgewählte Datei enthält kein Wurzelelement.");

        if (root.NamespaceURI != Ns)
            throw new InvalidOperationException("Die ausgewählte Datei ist keine XJustiz-Nachricht.");

        if (root.LocalName != ExpectedRoot)
            throw new InvalidOperationException("Die ausgewählte Datei ist keine eNoVA-Nachricht 2900003.");

        var fachdaten = GetDirectChild(root, "fachdaten");
        if (fachdaten is null)
            throw new InvalidOperationException("Die Nachricht enthält keine eNoVA-Fachdaten.");

        var gegenstand = GetDirectChild(fachdaten, "auswahl_GegenstandDerNachricht");
        if (gegenstand is null)
            throw new InvalidOperationException("Die Nachricht enthält keinen Gegenstand der Nachricht.");

        var ersuchen = GetDirectChild(gegenstand, "ersuchenSachentscheidung");
        if (ersuchen is null)
            throw new InvalidOperationException(
                "Die ausgewählte Nachricht ist kein Ersuchen um Sachentscheidung.");
    }

    private static XmlElement? GetDirectChild(XmlElement parent, string localName)
    {
        foreach (XmlNode node in parent.ChildNodes)
        {
            if (node is XmlElement el &&
                el.NamespaceURI == Ns &&
                el.LocalName == localName)
                return el;
        }
        return null;
    }
}
