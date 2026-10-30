using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Signs sample XML files on server start: some embedded, some detached.
/// </summary>
public static class SampleSigner
{
    public static void SignSamples(string samplesDir)
    {
        if (!Directory.Exists(samplesDir)) return;

        var files = Directory.GetFiles(samplesDir, "*.xml");
        var i = 0;
        foreach (var file in files)
        {
            try
            {
                if (i % 2 == 0)
                {
                    SignEmbedded(file);
                    Console.WriteLine($"[SampleSigner] {Path.GetFileName(file)} → embedded");
                }
                else
                {
                    SignDetached(file);
                    Console.WriteLine($"[SampleSigner] {Path.GetFileName(file)} → detached (.p7s)");
                }
                i++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SampleSigner] {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Console.WriteLine($"[SampleSigner] Signed {i} sample files.");
    }

    private static void SignEmbedded(string xmlPath)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(xmlPath);

        // Remove existing signature if any
        var existing = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        while (existing.Count > 0)
            existing[0]!.ParentNode?.RemoveChild(existing[0]!);

        using var cert = CreateMockCert();
        var signed = new SignedXml(doc) { SigningKey = cert.GetRSAPrivateKey() };
        var reference = new Reference("");
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signed.AddReference(reference);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signed.KeyInfo = keyInfo;
        signed.ComputeSignature();
        doc.DocumentElement?.AppendChild(doc.ImportNode(signed.GetXml(), true));
        doc.Save(xmlPath);
    }

    private static void SignDetached(string xmlPath)
    {
        var content = File.ReadAllBytes(xmlPath);
        using var cert = CreateMockCert();
        var contentInfo = new ContentInfo(content);
        var cms = new SignedCms(contentInfo, detached: true);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);
        var p7s = Path.ChangeExtension(xmlPath, ".p7s");
        File.WriteAllBytes(p7s, cms.Encode());
    }

    private static X509Certificate2 CreateMockCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=LRA-GRZ Mock, O=EGOV", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
}
