using System.Xml;

namespace Lragrz.Enova.Core.Xml;

public static class XmlDocumentExtensions
{
    /// <summary>
    /// Returns a pretty-printed UTF-8 string of the document (for debug / test output).
    /// </summary>
    public static string ToPrettyString(this XmlDocument document)
    {
        using var sw = new StringWriter();
        using var writer = XmlWriter.Create(sw, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false
        });
        document.Save(writer);
        return sw.ToString();
    }
}

