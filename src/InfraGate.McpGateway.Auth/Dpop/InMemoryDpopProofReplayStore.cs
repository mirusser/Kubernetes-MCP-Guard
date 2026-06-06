using System.Collections.Concurrent;

namespace InfraGate.McpGateway.Auth.Dpop;

internal sealed class InMemoryDpopProofReplayStore : IDpopProofReplayStore
{
    private readonly ConcurrentDictionary<(string Issuer, string Presenter, string Jti), DateTimeOffset> usedJtis =
        new();

    public Task<bool> TryAddAsync(
        string issuer,
        string presenter,
        string jti,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        PurgeExpired();
        var expiry = DateTimeOffset.UtcNow + lifetime;
        return Task.FromResult(usedJtis.TryAdd((issuer, presenter, jti), expiry));
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, expiry) in usedJtis)
        {
            if (expiry <= now)
                usedJtis.TryRemove(key, out _);
        }
    }
}
