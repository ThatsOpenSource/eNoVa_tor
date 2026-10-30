using System.Xml;
using Lragrz.Enova.Core.Config;
using Lragrz.Enova.Core.Crypto;
using Lragrz.Enova.Core.Models;
using Lragrz.Enova.Core.Xml;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lragrz.Enova.Tests;

/// <summary>
/// Decoupled verification checklist:
/// 1. Pure in-memory execution – no browser, no local web server.
/// 2. Enveloped XML-DSig structure is present after signing.
/// </summary>
public class InMemoryPipelineTests
{
    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

    [Fact]
    public async Task Process_SampleXml_CompletesInMemory_WithEnvelopedSignature()
    {
        // Arrange
        var samplePath = Path.Combine(AppContext.BaseDirectory, "samples", "debug-enova-2900003.xml");
        Assert.True(File.Exists(samplePath), $"Sample file missing: {samplePath}");

        var config = new EnovaConfiguration
        {
            CryptoProvider = "MOCK_FALLBACK",
            AuthorityIdentifier = "DE-MUSTERSTADT-08"
        };

        using var loggerFactory = CreateLoggerFactory();
        var factory = new QesProviderFactory(config, loggerFactory);
        var provider = factory.Create();
        var processor = new EnovaXmlProcessor(provider, loggerFactory.CreateLogger<EnovaXmlProcessor>());

        var answer = new AnswerParameters
        {
            FileNumber = "GEM-2026-7788",
            MessageId = "msg-answer-" + Guid.NewGuid().ToString("N"),
            CaseworkerName = "Antonia",
            DecisionCodes = new[] { "001" }
        };

        // Act – pure memory pipeline
        await using var stream = File.OpenRead(samplePath);
        var result = await processor.ProcessAsync(stream, answer);

        // Assert – provider used
        Assert.Equal("MOCK_FALLBACK", result.ProviderId);

        // Assert – Signature element present (enveloped)
        var nsmgr = new XmlNamespaceManager(result.SignedDocument.NameTable);
        nsmgr.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var signatureNode = result.SignedDocument.SelectSingleNode("//ds:Signature", nsmgr);
        Assert.NotNull(signatureNode);

        // Assert – Reference uses enveloped-signature transform
        var transformNode = result.SignedDocument.SelectSingleNode(
            "//ds:Signature/ds:SignedInfo/ds:Reference/ds:Transforms/ds:Transform[@Algorithm='http://www.w3.org/2000/09/xmldsig#enveloped-signature']",
            nsmgr);
        Assert.NotNull(transformNode);

        // Assert – AuthorityIdentifier property embedded
        var authority = result.SignedDocument.SelectSingleNode(
            "//*[local-name()='AuthorityIdentifier']");
        Assert.NotNull(authority);
        Assert.Equal("DE-MUSTERSTADT-08", authority.InnerText);

        // Write debug output for manual inspection
        var debugDir = Path.Combine(Path.GetTempPath(), "enova-egov-debug");
        Directory.CreateDirectory(debugDir);
        var outPath = Path.Combine(debugDir, $"signed-{DateTime.UtcNow:yyyyMMddHHmmss}.xml");
        result.SaveToFile(outPath);

        // Optional console visibility when running tests with -v n
        Console.WriteLine($"Debug signed XML written to: {outPath}");
    }

    [Fact]
    public async Task MockProvider_IsAlwaysAvailable()
    {
        using var loggerFactory = CreateLoggerFactory();
        var provider = new LocalMockQesProvider(
            loggerFactory.CreateLogger<LocalMockQesProvider>(),
            "DE-TEST");

        Assert.True(await provider.IsAvailableAsync());
    }
}

// Needed for the constant
file static class SignedXml
{
    public const string XmlDsigNamespaceUrl = "http://www.w3.org/2000/09/xmldsig#";
}


