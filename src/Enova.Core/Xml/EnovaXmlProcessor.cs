using System.Xml;
using Lragrz.Enova.Core.Crypto;
using Lragrz.Enova.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lragrz.Enova.Core.Xml;

/// <summary>
/// Pure in-memory processor for eNoVA 2900003 messages.
/// Load → DOM → answer transform → QES → final document.
/// Never opens a browser or starts a local HTTP listener.
/// </summary>
public sealed class EnovaXmlProcessor
{
    private readonly IQesProvider _qesProvider;
    private readonly ILogger<EnovaXmlProcessor> _logger;
    private readonly string _senderName;

    public EnovaXmlProcessor(
        IQesProvider qesProvider,
        ILogger<EnovaXmlProcessor> logger,
        string senderName = "LH München")
    {
        _qesProvider = qesProvider ?? throw new ArgumentNullException(nameof(qesProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _senderName = senderName;
    }

    public XmlDocument Load(Stream inputStream)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreWhitespace = false
        };

        using var reader = XmlReader.Create(inputStream, settings);
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(reader);

        _logger.LogDebug("XML loaded into memory. Root = {Root}", document.DocumentElement?.LocalName);
        return document;
    }

    public XmlDocument LoadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public async Task<ProcessingResult> ProcessAsync(
        Stream inputStream,
        AnswerParameters? answerParameters = null,
        CancellationToken cancellationToken = default)
    {
        var document = Load(inputStream);
        ErsuchenSachentscheidungChecker.Check(document);

        if (answerParameters is not null)
        {
            AnswerTransform.Apply(document, answerParameters, _senderName);
            _logger.LogInformation("Full answer transform applied.");
        }

        var signed = await _qesProvider
            .SignEnvelopedAsync(document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new ProcessingResult(signed, _qesProvider.ProviderId);
    }

    public async Task<ProcessingResult> ProcessFileAsync(
        string inputPath,
        AnswerParameters? answerParameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var fs = File.OpenRead(inputPath);
        return await ProcessAsync(fs, answerParameters, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateIsEnova2900003(XmlDocument document)
    {
        var root = document.DocumentElement
                   ?? throw new InvalidOperationException("Document has no root element.");

        const string ns = "http://www.xjustiz.de";
        if (root.NamespaceURI != ns ||
            root.LocalName != "nachricht.enova.entscheidung.2900003")
        {
            throw new InvalidOperationException(
                $"Not an eNoVA 2900003 message. Found: {{{root.NamespaceURI}}}{root.LocalName}");
        }
    }
}

