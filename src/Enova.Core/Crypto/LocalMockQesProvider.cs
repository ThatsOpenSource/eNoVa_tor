using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Lragrz.Enova.Core.Crypto;

/// <summary>
/// Offline fallback QES provider.
/// Generates an ephemeral self-signed certificate and performs a standards-compliant
/// enveloped XML-DSig signature entirely in memory. Instant success, no network.
/// </summary>
public sealed class LocalMockQesProvider : IQesProvider
{
    private readonly ILogger<LocalMockQesProvider> _logger;
    private readonly string _authorityIdentifier;
    private readonly Lazy<X509Certificate2> _certificate;

    public string ProviderId => "MOCK_FALLBACK";
    public string DisplayName => "Local Mock (Self-Signed)";

    public LocalMockQesProvider(ILogger<LocalMockQesProvider> logger, string authorityIdentifier)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authorityIdentifier = string.IsNullOrWhiteSpace(authorityIdentifier)
            ? "DE-MUSTERSTADT-08"
            : authorityIdentifier;

        _certificate = new Lazy<X509Certificate2>(CreateSelfSignedCertificate);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Always available offline
        return Task.FromResult(true);
    }

    public Task<XmlDocument> SignEnvelopedAsync(
        XmlDocument document,
        string? elementToSign = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _logger.LogInformation("MOCK_FALLBACK: Starting in-memory enveloped signature for authority {Authority}",
            _authorityIdentifier);

        var cert = _certificate.Value;

        // Ensure the document has an Id on the root if we need a reference
        var root = document.DocumentElement
                   ?? throw new InvalidOperationException("Document has no root element.");

        if (string.IsNullOrEmpty(root.GetAttribute("Id")))
        {
            root.SetAttribute("Id", "enova-root-" + Guid.NewGuid().ToString("N"));
        }

        var signedXml = new SignedXml(document)
        {
            SigningKey = cert.GetRSAPrivateKey()
                         ?? throw new InvalidOperationException("Certificate has no RSA private key.")
        };

        // Enveloped-signature transform (removes the Signature element itself from the digest)
        var reference = new Reference
        {
            Uri = "#" + root.GetAttribute("Id")
        };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        // KeyInfo with the public certificate
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;

        // Optional: add authority identifier as SignatureProperty for audit
        var sp = document.CreateElement("SignatureProperty", SignedXml.XmlDsigNamespaceUrl);
        sp.SetAttribute("Target", "#" + root.GetAttribute("Id"));
        var authorityNode = document.CreateElement("AuthorityIdentifier", "urn:enova:egov:v1");
        authorityNode.InnerText = _authorityIdentifier;
        sp.AppendChild(authorityNode);

        var signedProps = document.CreateElement("SignatureProperties", SignedXml.XmlDsigNamespaceUrl);
        signedProps.AppendChild(sp);

        var objectNode = document.CreateElement("Object", SignedXml.XmlDsigNamespaceUrl);
        objectNode.AppendChild(signedProps);
        signedXml.AddObject(new DataObject { Data = objectNode.ChildNodes, Id = "enova-props" });

        signedXml.ComputeSignature();

        var xmlDigitalSignature = signedXml.GetXml();
        root.AppendChild(document.ImportNode(xmlDigitalSignature, true));

        _logger.LogInformation("MOCK_FALLBACK: Enveloped signature successfully embedded.");

        return Task.FromResult(document);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=Enova EGOV Mock QES, O=EGOV, C=DE",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        // Valid for 5 years – purely local mock
        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        // Export + re-import to obtain a certificate that has the private key exportable
        // (required by SignedXml on some runtimes)
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx),
            password: null,
            X509KeyStorageFlags.Exportable);
    }
}

