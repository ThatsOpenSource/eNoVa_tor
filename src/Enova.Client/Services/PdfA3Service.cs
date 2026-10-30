using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Filespec;
using iText.Kernel.XMP;
using iText.Signatures;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Creates and verifies PDF/A-3 containers that embed a signed eNoVA XML.
/// </summary>
public static class PdfA3Service
{
    public sealed class VerificationResult
    {
        public bool IsPdfA3 { get; init; }
        public bool HasEmbeddedXml { get; init; }
        public bool PdfSignaturePresent { get; init; }
        public bool XmlSignatureValid { get; init; }
        public bool AuthorityMatches { get; init; }
        public string? EmbeddedFileName { get; init; }
        public string? ExtractedXmlPath { get; init; }
        public string? AuthorityIdentifier { get; init; }
        public string? Caseworker { get; init; }
        public string? FileNumber { get; init; }
        public string? DecisionLabel { get; init; }
        public string? SenderName { get; init; }
        public string Summary { get; init; } = "";
        public string? Error { get; init; }
    }

    /// <summary>
    /// Takes a visual PDF and embeds the signed XML as an Associated File, marking the result as PDF/A-3.
    /// </summary>
    public static void EmbedXmlAsPdfA3(string visualPdfPath, string signedXmlPath, string outputPdfPath, string authorityId, string senderName, string? detachedSignaturePath = null)
    {
        var xmlBytes = File.ReadAllBytes(signedXmlPath);
        var xmlName = Path.GetFileName(signedXmlPath);

        using var reader = new PdfReader(visualPdfPath);
        using var writer = new PdfWriter(outputPdfPath);
        using var pdf = new PdfDocument(reader, writer);

        // Embed as Associated File (PDF/A-3 style)
        var fs = PdfFileSpec.CreateEmbeddedFileSpec(
            pdf,
            xmlBytes,
            "Signierte eNoVA-Antwort",
            xmlName,
            PdfName.ApplicationXml,
            null,
            new PdfName("Data"));

        pdf.AddFileAttachment(xmlName, fs);

        // Embed the original detached signature if the request used one
        if (!string.IsNullOrEmpty(detachedSignaturePath) && File.Exists(detachedSignaturePath))
        {
            var p7sName = Path.GetFileName(detachedSignaturePath);
            var p7sSpec = PdfFileSpec.CreateEmbeddedFileSpec(
                pdf, File.ReadAllBytes(detachedSignaturePath), p7sName, p7sName,
                new PdfName("application/pkcs7-signature"), null, new PdfName("Data"));
            pdf.AddFileAttachment(p7sName, p7sSpec);
        }

        // Set PDF/A-3 identification via XMP (string packet so namespaces are declared)
        var xmp = $"""
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/">
                  <pdfaid:part>3</pdfaid:part>
                  <pdfaid:conformance>B</pdfaid:conformance>
                </rdf:Description>
                <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title><rdf:Alt><rdf:li xml:lang="x-default">eNoVA Entscheidung – {Escape(senderName)}</rdf:li></rdf:Alt></dc:title>
                </rdf:Description>
                <rdf:Description rdf:about="" xmlns:enova="http://enova.lragrz.de/ns/">
                  <enova:authority>{Escape(authorityId)}</enova:authority>
                  <enova:sender>{Escape(senderName)}</enova:sender>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        var xmpMeta = XMPMetaFactory.ParseFromString(xmp);
        pdf.SetXmpMetadata(xmpMeta);

        var info = pdf.GetDocumentInfo();
        info.SetTitle($"eNoVA – {senderName}");
        info.SetAuthor(senderName);
        info.SetSubject($"Authority: {authorityId}");
        info.SetKeywords("eNoVA,PDF/A-3,signed-xml");

        pdf.Close();

        // Apply a digital signature to the PDF itself (PAdES-style, mock cert)
        SignPdf(outputPdfPath, authorityId, senderName);
    }

    private static void SignPdf(string pdfPath, string authorityId, string senderName)
    {
        var signedPath = pdfPath + ".signed.pdf";
        using var cert = CreateSigningCertificate(senderName, authorityId);

        using var reader = new PdfReader(pdfPath);
        using var os = new FileStream(signedPath, FileMode.Create);
        var signer = new PdfSigner(reader, os, new StampingProperties());

        signer.SetReason($"eNoVA Entscheidung – {authorityId}");
        signer.SetLocation(senderName);
        signer.SetContact(senderName);

        // Use iText's BC wrappers to avoid BouncyCastle assembly conflict
        var bcCert = new X509CertificateBC(new Org.BouncyCastle.X509.X509CertificateParser()
            .ReadCertificate(cert.RawData));
        var chain = new IX509Certificate[] { bcCert };

        using var rsa = cert.GetRSAPrivateKey()!;
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        var bcKey = Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(pkcs8);
        var pk = new iText.Bouncycastle.Crypto.PrivateKeyBC(bcKey);
        IExternalSignature signature = new PrivateKeySignature(pk, DigestAlgorithms.SHA256);

        signer.SignDetached(signature, chain, null, null, null, 0, PdfSigner.CryptoStandard.CMS);

        File.Delete(pdfPath);
        File.Move(signedPath, pdfPath);
    }

    private static X509Certificate2 CreateSigningCertificate(string sender, string authority)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            $"CN={sender}, O={authority}, OU=eNoVA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Opens a PDF, checks for PDF/A-3 markers and an embedded signed XML,
    /// validates the XML signature and agency identity, writes the XML next to the PDF.
    /// </summary>
    public static VerificationResult VerifyAndExtract(string pdfPath, string expectedAuthority)
    {
        try
        {
            using var reader = new PdfReader(pdfPath);
            using var pdf = new PdfDocument(reader);

            var isA3 = CheckPdfA3(pdf);
            var pdfSigned = pdf.GetNumberOfPdfObjects() > 0 && HasPdfSignature(pdf);

            var catalog = pdf.GetCatalog();
            var names = catalog.GetNameTree(PdfName.EmbeddedFiles);
            if (names is null || names.GetNames() is null || names.GetNames().Count == 0)
            {
                return new VerificationResult
                {
                    IsPdfA3 = isA3,
                    HasEmbeddedXml = false,
                    Summary = "Keine eingebettete Datei gefunden.",
                    Error = "Der PDF enthält keine eingebettete XML-Datei."
                };
            }

            string? extractedPath = null;
            string? fileName = null;
            byte[]? xmlBytes = null;

            foreach (var entry in names.GetNames())
            {
                var name = entry.Key?.ToString() ?? "";
                if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fsDict = entry.Value as PdfDictionary;
                if (fsDict is null) continue;

                var ef = fsDict.GetAsDictionary(PdfName.EF);
                var stream = ef?.GetAsStream(PdfName.F);
                if (stream is null) continue;

                xmlBytes = stream.GetBytes();
                fileName = name;
                break;
            }

            if (xmlBytes is null || fileName is null)
            {
                return new VerificationResult
                {
                    IsPdfA3 = isA3,
                    HasEmbeddedXml = false,
                    Summary = "Keine XML-Datei in den eingebetteten Dateien gefunden."
                };
            }

            var dir = Path.GetDirectoryName(pdfPath) ?? ".";
            var outName = Path.GetFileNameWithoutExtension(pdfPath) + "-extracted.xml";
            extractedPath = Path.Combine(dir, outName);
            File.WriteAllBytes(extractedPath, xmlBytes);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(Encoding.UTF8.GetString(xmlBytes));

            var sigValid = ValidateXmlSignature(doc);
            var authority = ExtractAuthority(doc);
            var authorityOk = string.Equals(authority, expectedAuthority, StringComparison.OrdinalIgnoreCase);
            var caseworker = ExtractText(doc, "//tns:nachname") ?? ExtractText(doc, "//*[local-name()='nachname']");
            var az = ExtractText(doc, "//tns:aktenzeichen");
            var sender = ExtractText(doc, "//tns:sonstige") ?? ExtractText(doc, "//*[local-name()='sonstige']");

            var summary = new StringBuilder();
            summary.AppendLine(isA3 ? "PDF/A-3 Kennzeichnung vorhanden." : "Keine PDF/A-3 Kennzeichnung gefunden.");
            summary.AppendLine(pdfSigned ? "PDF-Signatur: vorhanden." : "PDF-Signatur: nicht gefunden.");
            summary.AppendLine($"Eingebettete Datei: {fileName}");
            summary.AppendLine(sigValid ? "XML-Signatur: gültig." : "XML-Signatur: ungültig oder nicht vorhanden.");
            summary.AppendLine($"Behörde: {authority ?? "—"} {(authorityOk ? "(stimmt überein)" : "(weicht ab)")}");
            summary.AppendLine($"Absender: {sender ?? "—"}");
            summary.AppendLine($"Aktenzeichen: {az ?? "—"}");
            summary.AppendLine($"XML geschrieben nach: {extractedPath}");

            return new VerificationResult
            {
                IsPdfA3 = isA3,
                HasEmbeddedXml = true,
                PdfSignaturePresent = pdfSigned,
                XmlSignatureValid = sigValid,
                AuthorityMatches = authorityOk,
                EmbeddedFileName = fileName,
                ExtractedXmlPath = extractedPath,
                AuthorityIdentifier = authority,
                Caseworker = caseworker,
                FileNumber = az,
                SenderName = sender,
                Summary = summary.ToString().Trim()
            };
        }
        catch (Exception ex)
        {
            return new VerificationResult
            {
                Error = ex.Message,
                Summary = "Prüfung fehlgeschlagen: " + ex.Message
            };
        }
    }

    private static bool HasPdfSignature(PdfDocument pdf)
    {
        try
        {
            var sigUtil = new SignatureUtil(pdf);
            var names = sigUtil.GetSignatureNames();
            return names != null && names.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckPdfA3(PdfDocument pdf)
    {
        try
        {
            var xmp = pdf.GetXmpMetadata();
            if (xmp is null || xmp.Length == 0) return false;
            var text = Encoding.UTF8.GetString(xmp);
            return text.Contains("pdfaid") && (text.Contains("part") || text.Contains(">3<") || text.Contains("3"));
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateXmlSignature(XmlDocument doc)
    {
        try
        {
            var signed = new SignedXml(doc);
            var sigNode = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (sigNode.Count == 0) return false;
            signed.LoadXml((XmlElement)sigNode[0]!);
            return signed.CheckSignature();
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractAuthority(XmlDocument doc)
    {
        var nodes = doc.GetElementsByTagName("AuthorityIdentifier");
        if (nodes.Count > 0) return nodes[0]?.InnerText;
        // Fallback: look in XAdES or custom
        return null;
    }

    private static string? ExtractText(XmlDocument doc, string xpath)
    {
        try
        {
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("tns", "http://www.xjustiz.de");
            return doc.SelectSingleNode(xpath, nsmgr)?.InnerText?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string Escape(string s) =>
        System.Net.WebUtility.HtmlEncode(s);
}
