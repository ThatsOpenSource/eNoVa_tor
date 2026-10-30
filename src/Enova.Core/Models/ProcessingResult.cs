using System.Xml;

namespace Lragrz.Enova.Core.Models;

/// <summary>
/// Result of a complete in-memory processing run.
/// </summary>
public sealed class ProcessingResult
{
    public ProcessingResult(XmlDocument signedDocument, string providerId)
    {
        SignedDocument = signedDocument ?? throw new ArgumentNullException(nameof(signedDocument));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public XmlDocument SignedDocument { get; }
    public string ProviderId { get; }
    public DateTime ProcessedAtUtc { get; }

    /// <summary>
    /// Serialises the signed document to a UTF-8 byte array (no BOM).
    /// </summary>
    public byte[] ToUtf8Bytes()
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        }))
        {
            SignedDocument.Save(writer);
        }
        return ms.ToArray();
    }

    public void SaveToFile(string path)
    {
        var bytes = ToUtf8Bytes();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, bytes);
    }
}

