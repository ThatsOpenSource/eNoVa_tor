using System.Collections.Concurrent;

namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Simple fixed-window rate limiter for login attempts (per IP).
/// </summary>
public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _attempts = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;

    public LoginRateLimiter(int maxAttempts = 10, int windowSeconds = 60)
    {
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public bool IsAllowed(string key)
    {
        var now = DateTime.UtcNow;
        var entry = _attempts.AddOrUpdate(
            key,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart > _window)
                    return (1, now);
                return (existing.Count + 1, existing.WindowStart);
            });

        return entry.Count <= _maxAttempts;
    }
}
