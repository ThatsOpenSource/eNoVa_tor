using System;
using System.Net.Http;

namespace Lragrz.Enova.Client.Services;

internal static class EnovaHttp
{
    /// <summary>
    /// HttpClient that accepts the ASP.NET development certificate for localhost only.
    /// Prefer http://localhost for local server to avoid cert issues entirely.
    /// </summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (message, cert, chain, errors) =>
            {
                var host = message.RequestUri?.Host;
                if (host is "localhost" or "127.0.0.1" or "::1")
                    return true;
                return errors == System.Net.Security.SslPolicyErrors.None;
            }
        };

        return new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
    }
}

