using Lragrz.Enova.Core.Config;
using Microsoft.Extensions.Logging;

namespace Lragrz.Enova.Core.Crypto;

/// <summary>
/// Resolves the concrete IQesProvider from configuration.
/// </summary>
public sealed class QesProviderFactory
{
    private readonly EnovaConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;

    public QesProviderFactory(EnovaConfiguration config, ILoggerFactory loggerFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IQesProvider Create()
    {
        var providerKey = (_config.CryptoProvider ?? "MOCK_FALLBACK").Trim().ToUpperInvariant();

        return providerKey switch
        {
            "MOCK_FALLBACK" or "MOCK" => new LocalMockQesProvider(
                _loggerFactory.CreateLogger<LocalMockQesProvider>(),
                _config.AuthorityIdentifier),

            "VDA_PRODUCTION" or "VDA" or "PRODUCTION" => new ProductionVdaProvider(
                _loggerFactory.CreateLogger<ProductionVdaProvider>(),
                _config.VdaRestEndpoint,
                _config.AuthorityIdentifier,
                _config.ServerBaseUrl),

            _ => throw new InvalidOperationException(
                $"Unknown crypto provider '{_config.CryptoProvider}'. Allowed: MOCK_FALLBACK, VDA_PRODUCTION.")
        };
    }
}

