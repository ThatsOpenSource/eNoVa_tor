using System.Xml;
using Lragrz.Enova.Core.Models;

namespace Lragrz.Enova.Core.Xml;

/// <summary>
/// In-memory equivalent of the Java answer-transform.xsl.
/// Applies the essential structural changes required for an eNoVA answer message.
/// </summary>
public static class AnswerTransform
{
    private const string Ns = "http://www.xjustiz.de";

    public static void Apply(XmlDocument document, AnswerParameters parameters, string senderName = "LH München")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parameters);

        var nsmgr = CreateNs(document);

        UpdateSchemaLocation(document);
        UpdateNachrichtenkopf(document, nsmgr, parameters, senderName);
        UpdateInstanzbehoerde(document, nsmgr, senderName);
        SwapAbsenderEmpfaenger(document, nsmgr);
        InjectCaseworkerBeteiligung(document, nsmgr, parameters);
        ReplaceErsuchenWithSachentscheidung(document, nsmgr, parameters);
        ReplaceSchriftgutobjekte(document, nsmgr);
    }

    private static XmlNamespaceManager CreateNs(XmlDocument document)
    {
        var nsmgr = new XmlNamespaceManager(document.NameTable);
        nsmgr.AddNamespace("tns", Ns);
        nsmgr.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        return nsmgr;
    }

    private static void UpdateSchemaLocation(XmlDocument document)
    {
        var root = document.DocumentElement!;
        root.SetAttribute(
            "schemaLocation",
            "http://www.w3.org/2001/XMLSchema-instance",
            "http://www.xjustiz.de xjustiz_2900_dabag_3_3.xsd");
    }

    private static void UpdateNachrichtenkopf(
        XmlDocument document,
        XmlNamespaceManager nsmgr,
        AnswerParameters parameters,
        string senderName)
    {
        // Erstellungszeitpunkt
        SetInnerText(document, nsmgr,
            "//tns:nachrichtenkopf/tns:erstellungszeitpunkt",
            DateTime.UtcNow.ToString("o"));

        // Absender Aktenzeichen
        SetInnerText(document, nsmgr,
            "//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen",
            parameters.FileNumber);

        // Eigene Nachrichten-ID
        SetInnerText(document, nsmgr,
            "//tns:nachrichtenkopf/tns:absender/tns:eigeneNachrichtenID",
            parameters.MessageId);

        // Absender Kommunikationspartner
        SetInnerText(document, nsmgr,
            "//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_kommunikationspartner/tns:sonstige",
            senderName);

        // Herstellerinformation – remove old, append new
        var kopf = document.SelectSingleNode("//tns:nachrichtenkopf", nsmgr) as XmlElement;
        if (kopf is not null)
        {
            foreach (XmlNode old in kopf.SelectNodes("tns:herstellerinformation", nsmgr)!)
                kopf.RemoveChild(old);

            var hersteller = document.CreateElement("herstellerinformation", Ns);

            AppendTextElement(document, hersteller, "nameDesProdukts",
                parameters.ProductName ?? "LHM-eNoVA-Generator");
            AppendTextElement(document, hersteller, "herstellerDesProdukts",
                parameters.ManufacturerName ?? "EGOV");
            AppendTextElement(document, hersteller, "version",
                parameters.Version ?? "0.0.1");

            kopf.AppendChild(hersteller);
        }
    }

    private static void UpdateInstanzbehoerde(
        XmlDocument document,
        XmlNamespaceManager nsmgr,
        string senderName)
    {
        SetInnerText(document, nsmgr,
            "//tns:grunddaten/tns:verfahrensdaten/tns:instanzdaten/tns:auswahl_instanzbehoerde/tns:sonstige",
            senderName);
    }

    /// <summary>
    /// Turns the original absender into the new empfaenger (keeps foreign message id & file number).
    /// </summary>
    private static void SwapAbsenderEmpfaenger(XmlDocument document, XmlNamespaceManager nsmgr)
    {
        var kopf = document.SelectSingleNode("//tns:nachrichtenkopf", nsmgr) as XmlElement;
        if (kopf is null) return;

        var oldAbsender = kopf.SelectSingleNode("tns:absender", nsmgr) as XmlElement;
        var oldEmpfaenger = kopf.SelectSingleNode("tns:empfaenger", nsmgr) as XmlElement;
        if (oldAbsender is null) return;

        // Capture original absender values before we overwrite them
        var originalAktenzeichen = oldAbsender.SelectSingleNode("tns:aktenzeichen", nsmgr)?.InnerText ?? "";
        var originalMsgId = oldAbsender.SelectSingleNode("tns:eigeneNachrichtenID", nsmgr)?.InnerText ?? "";

        // Build new empfaenger from original absender data
        var newEmpfaenger = document.CreateElement("empfaenger", Ns);

        var info = document.CreateElement("informationen", Ns);
        var komPartner = oldAbsender.SelectSingleNode(
            "tns:informationen/tns:auswahl_kommunikationspartner", nsmgr);
        if (komPartner is not null)
            info.AppendChild(document.ImportNode(komPartner, true));
        newEmpfaenger.AppendChild(info);

        var az = document.CreateElement("auswahl_aktenzeichen", Ns);
        var azFree = document.CreateElement("aktenzeichen.freitext", Ns);
        azFree.InnerText = originalAktenzeichen;
        az.AppendChild(azFree);
        newEmpfaenger.AppendChild(az);

        var fremdeId = document.CreateElement("fremdeNachrichtenID", Ns);
        fremdeId.InnerText = originalMsgId;
        newEmpfaenger.AppendChild(fremdeId);

        if (oldEmpfaenger is not null)
            kopf.ReplaceChild(newEmpfaenger, oldEmpfaenger);
        else
            kopf.AppendChild(newEmpfaenger);
    }

    private static void InjectCaseworkerBeteiligung(
        XmlDocument document,
        XmlNamespaceManager nsmgr,
        AnswerParameters parameters)
    {
        var verfahrensdaten = document.SelectSingleNode(
            "//tns:grunddaten/tns:verfahrensdaten", nsmgr) as XmlElement;
        if (verfahrensdaten is null) return;

        // Next free role / participant numbers
        int nextRole = 1;
        int nextParticipant = 1;
        foreach (XmlNode n in document.SelectNodes("//tns:rollennummer", nsmgr)!)
            if (int.TryParse(n.InnerText, out var v) && v >= nextRole) nextRole = v + 1;
        foreach (XmlNode n in document.SelectNodes("//tns:beteiligtennummer", nsmgr)!)
            if (int.TryParse(n.InnerText, out var v) && v >= nextParticipant) nextParticipant = v + 1;

        var beteiligung = document.CreateElement("beteiligung", Ns);

        var rolle = document.CreateElement("rolle", Ns);
        AppendTextElement(document, rolle, "rollennummer", nextRole.ToString());
        var rollenbez = document.CreateElement("rollenbezeichnung", Ns);
        rollenbez.SetAttribute("listVersionID", "3.5");
        AppendTextElement(document, rollenbez, "code", "212"); // Sachbearbeiter
        rolle.AppendChild(rollenbez);
        beteiligung.AppendChild(rolle);

        var beteiligter = document.CreateElement("beteiligter", Ns);
        AppendTextElement(document, beteiligter, "beteiligtennummer", nextParticipant.ToString());

        var auswahl = document.CreateElement("auswahl_beteiligter", Ns);
        var person = document.CreateElement("natuerlichePerson", Ns);
        var name = document.CreateElement("vollerName", Ns);
        // Split CaseworkerName into vorname/nachname if possible
        var parts = parameters.CaseworkerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        AppendTextElement(document, name, "vorname", parts.Length > 0 ? parts[0] : parameters.CaseworkerName);
        AppendTextElement(document, name, "nachname", parts.Length > 1 ? parts[1] : "");
        person.AppendChild(name);
        auswahl.AppendChild(person);
        beteiligter.AppendChild(auswahl);
        beteiligung.AppendChild(beteiligter);

        verfahrensdaten.AppendChild(beteiligung);

        // Point absender ref.rollennummer to the new role
        SetInnerText(document, nsmgr,
            "//tns:nachrichtenkopf/tns:absender/tns:informationen/tns:auswahl_verweisGrunddaten/tns:ref.rollennummer",
            nextRole.ToString());
    }

    private static void ReplaceErsuchenWithSachentscheidung(
        XmlDocument document,
        XmlNamespaceManager nsmgr,
        AnswerParameters parameters)
    {
        var gegenstand = document.SelectSingleNode(
            "//tns:fachdaten/tns:auswahl_GegenstandDerNachricht", nsmgr) as XmlElement;
        if (gegenstand is null) return;

        // Collect role numbers of Käufer (072) and Verkäufer (168)
        var addresseeRoles = new List<string>();
        foreach (XmlNode rolle in document.SelectNodes(
                     "//tns:beteiligung[tns:rolle/tns:rollenbezeichnung/code='072' or tns:rolle/tns:rollenbezeichnung/code='168']/tns:rolle/tns:rollennummer",
                     nsmgr)!)
        {
            addresseeRoles.Add(rolle.InnerText.Trim());
        }

        // Remove all ersuchenSachentscheidung children
        var ersuchenNodes = gegenstand.SelectNodes("tns:ersuchenSachentscheidung", nsmgr)!;
        var codes = parameters.DecisionCodes.ToList();

        // We rebuild: for each original ersuchen (or at least one) create a sachentscheidung
        var count = Math.Max(ersuchenNodes.Count, codes.Count == 0 ? 1 : codes.Count);
        var toRemove = new List<XmlNode>();
        foreach (XmlNode n in ersuchenNodes) toRemove.Add(n);
        foreach (var n in toRemove) gegenstand.RemoveChild(n);

        for (var i = 0; i < count; i++)
        {
            var code = codes.Count > i ? codes[i] : (codes.Count > 0 ? codes[^1] : "001");

            var sach = document.CreateElement("sachentscheidung", Ns);
            var sachCode = document.CreateElement("sachentscheidung", Ns);
            sachCode.SetAttribute("listVersionID", "1.1");
            AppendTextElement(document, sachCode, "code", code);
            sach.AppendChild(sachCode);

            foreach (var roleNum in addresseeRoles)
            {
                var adressat = document.CreateElement("adressatDerSachentscheidung", Ns);
                var refRole = document.CreateElement("ref.rollennummer", Ns);
                AppendTextElement(document, refRole, "ref.rollennummer", roleNum);
                adressat.AppendChild(refRole);
                sach.AppendChild(adressat);
            }

            gegenstand.AppendChild(sach);
        }
    }

    private static void ReplaceSchriftgutobjekte(XmlDocument document, XmlNamespaceManager nsmgr)
    {
        var root = document.DocumentElement!;
        var existing = root.SelectSingleNode("tns:schriftgutobjekte", nsmgr);

        var sgo = document.CreateElement("schriftgutobjekte", Ns);
        var dok = document.CreateElement("dokument", Ns);

        var ident = document.CreateElement("identifikation", Ns);
        AppendTextElement(document, ident, "id", Guid.NewGuid().ToString());
        AppendTextElement(document, ident, "nummerImUebergeordnetenContainer", "1");
        dok.AppendChild(ident);

        var fach = document.CreateElement("xjustiz.fachspezifischeDaten", Ns);

        var dokKlasse = document.CreateElement("dokumentklasse", Ns);
        dokKlasse.SetAttribute("listVersionID", "1.3");
        AppendTextElement(document, dokKlasse, "code", "017"); // Bescheid
        fach.AppendChild(dokKlasse);

        var dokTyp = document.CreateElement("dokumententyp", Ns);
        dokTyp.SetAttribute("listVersionID", "4.1");
        AppendTextElement(document, dokTyp, "code", "105");
        fach.AppendChild(dokTyp);

        var datei = document.CreateElement("datei", Ns);
        AppendTextElement(document, datei, "dateiname", "xjustiz_nachricht.xml");
        var bestandteil = document.CreateElement("bestandteil", Ns);
        bestandteil.SetAttribute("listVersionID", "2.5");
        AppendTextElement(document, bestandteil, "code", "001");
        datei.AppendChild(bestandteil);
        fach.AppendChild(datei);

        dok.AppendChild(fach);
        sgo.AppendChild(dok);

        if (existing is not null)
            root.ReplaceChild(sgo, existing);
        else
            root.AppendChild(sgo);
    }

    // ----- helpers -----

    private static void SetInnerText(
        XmlDocument document,
        XmlNamespaceManager nsmgr,
        string xpath,
        string value)
    {
        if (document.SelectSingleNode(xpath, nsmgr) is XmlElement el)
            el.InnerText = value;
    }

    private static void AppendTextElement(
        XmlDocument document,
        XmlElement parent,
        string localName,
        string value)
    {
        var el = document.CreateElement(localName, Ns);
        el.InnerText = value;
        parent.AppendChild(el);
    }
}

