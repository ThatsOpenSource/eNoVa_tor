using System.Xml;

namespace Lragrz.Enova.Core.Crypto;

/// <summary>
/// Pluggable Qualified Electronic Signature (QES) provider contract.
/// Implementations perform in-place enveloped XML-DSig signing.
/// </summary>
public interface IQesProvider
{
    /// <summary>
    /// Provider identifier used in configuration (e.g. MOCK_FALLBACK, VDA_PRODUCTION).
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Returns true when the provider is ready for signing (keys loaded / endpoint reachable).
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs the given XML document in-place using an enveloped signature.
    /// The signature is embedded according to XML-DSig (http://www.w3.org/2000/09/xmldsig#).
    /// The method must not open browsers, start HTTP listeners or leave the process.
    /// </summary>
    /// <param name="document">Mutable XmlDocument already loaded in memory.</param>
    /// <param name="elementToSign">Optional XPath or Id of the element that becomes the signature reference. Null = whole document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The same document instance with the Signature element injected.</returns>
    Task<XmlDocument> SignEnvelopedAsync(
        XmlDocument document,
        string? elementToSign = null,
        CancellationToken cancellationToken = default);
}

