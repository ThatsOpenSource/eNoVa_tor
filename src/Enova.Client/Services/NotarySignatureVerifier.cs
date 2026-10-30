using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Security.Cryptography.Xml;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Dual-mode verification for notary requests: detached CAdES (.p7s) or embedded XAdES.
/// In mock mode, self-signed certificates pass on mathematical integrity only.
/// </summary>
public static class NotarySignatureVerifier
{
    public sealed class Result
    {
        public bool IsVerified { get; set; }
        public bool IsMock { get; set; }
        public string Mode { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? Signer { get; set; }
    }

    public static Result Verify(string xmlPath)
    {
        var dir = Path.GetDirectoryName(xmlPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(xmlPath);
        var p7s = Path.Combine(dir, baseName + ".p7s");
        if (!File.Exists(p7s))
            p7s = Path.Combine(dir, baseName + ".p7b");
        if (!File.Exists(p7s))
            p7s = Path.Combine(dir, baseName + ".pkcs7");

        if (File.Exists(p7s))
            return VerifyDetached(xmlPath, p7s);
        return VerifyEmbedded(xmlPath);
    }

    public static Result VerifyWithDetached(string xmlPath, string p7sPath) =>
        VerifyDetached(xmlPath, p7sPath);

    private static Result VerifyDetached(string xmlPath, string p7sPath)
    {
        try
        {
            var content = File.ReadAllBytes(xmlPath);
            var sig = File.ReadAllBytes(p7sPath);
            var cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(sig);

            var isMock = IsMockCert(cms);
            // Mock: verify math only. Production: also check trust.
            cms.CheckSignature(verifySignatureOnly: isMock);

            var signer = cms.SignerInfos[0].Certificate?.Subject ?? "?";
            return new Result
            {
                IsVerified = true,
                IsMock = isMock,
                Mode = "Detached CAdES",
                Signer = signer,
                Summary = isMock
                    ? $"Detached Signatur mathematisch gültig (Mock: {signer})."
                    : $"Detached Signatur gültig ({signer})."
            };
        }
        catch (Exception ex)
        {
            return new Result { IsVerified = false, Mode = "Detached CAdES", Summary = "Detached Prüfung fehlgeschlagen: " + ex.Message };
        }
    }

    private static Result VerifyEmbedded(string xmlPath)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(xmlPath);
            var signed = new SignedXml(doc);
            var sigNode = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (sigNode.Count == 0)
            {
                return new Result
                {
                    IsVerified = false,
                    IsMock = true,
                    Mode = "Keine Signatur",
                    Summary = "Keine eingebettete Signatur. Bitte die zugehörige .p7s-Datei angeben."
                };
            }

            signed.LoadXml((XmlElement)sigNode[0]!);
            var ok = signed.CheckSignature();
            return new Result
            {
                IsVerified = ok,
                Mode = "Embedded XAdES",
                Summary = ok ? "Eingebettete XML-Signatur gültig." : "Eingebettete XML-Signatur ungültig."
            };
        }
        catch (Exception ex)
        {
            return new Result { IsVerified = false, Mode = "Embedded", Summary = "Embedded Prüfung fehlgeschlagen: " + ex.Message };
        }
    }

    private static bool IsMockCert(SignedCms cms)
    {
        foreach (var si in cms.SignerInfos)
        {
            var cert = si.Certificate;
            if (cert is null) continue;
            if (cert.Subject.Contains("LRA-GRZ", StringComparison.OrdinalIgnoreCase)
                || cert.Issuer == cert.Subject)
                return true;
        }
        return false;
    }
}
