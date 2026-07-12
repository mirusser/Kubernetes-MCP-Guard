using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InfraGate.McpGateway.Auth;

internal sealed class TokenIntrospectionActivityValidator(
    ITokenIntrospectionClient introspectionClient,
    GatewayAuthOptions options,
    TimeProvider timeProvider) : ITokenActivityValidator
{
    private const int PruneThreshold = 1024;
    private readonly ConcurrentDictionary<string, DateTimeOffset> activeTokenCache = new(StringComparer.Ordinal);

    public async Task<bool> IsActiveAsync(JsonWebToken accessToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);

        string cacheKey = ComputeCacheKey(accessToken.EncodedToken);
        var now = timeProvider.GetUtcNow();
        if (activeTokenCache.TryGetValue(cacheKey, out var cachedUntil))
        {
            if (cachedUntil > now)
            {
                return true;
            }

            activeTokenCache.TryRemove(cacheKey, out _);
        }

        var result = await introspectionClient.IntrospectAsync(accessToken, cancellationToken).ConfigureAwait(false);
        if (!result.IsActive)
        {
            return false;
        }

        var cacheUntil = CalculateCacheExpiration(accessToken, result.ExpiresAt, now);
        if (cacheUntil <= now)
        {
            return true;
        }

        activeTokenCache[cacheKey] = cacheUntil;
        MaybePruneExpiredEntries();
        return true;
    }

    private void MaybePruneExpiredEntries()
    {
        if (activeTokenCache.Count > PruneThreshold)
        {
            PruneExpiredEntries();
        }
    }

    internal void PruneExpiredEntries()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in activeTokenCache)
        {
            if (entry.Value <= now)
            {
                activeTokenCache.TryRemove(entry.Key, out _);
            }
        }
    }

    private DateTimeOffset CalculateCacheExpiration(
        JsonWebToken accessToken,
        DateTimeOffset? introspectionExpiresAt,
        DateTimeOffset now)
    {
        if (options.TokenIntrospectionCacheSeconds <= 0)
        {
            return now;
        }

        var cacheUntil = now.AddSeconds(options.TokenIntrospectionCacheSeconds);
        if (TokenClaimDates.TryGetUnixTimeClaim(
                accessToken,
                GatewayAuthConventions.Claims.Expiration,
                out var tokenExpiresAt) &&
            tokenExpiresAt < cacheUntil)
        {
            cacheUntil = tokenExpiresAt;
        }

        if (introspectionExpiresAt is { } expiresAt && expiresAt < cacheUntil)
        {
            cacheUntil = expiresAt;
        }

        return cacheUntil;
    }

    private static string ComputeCacheKey(string accessToken)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessToken));
        return Convert.ToHexString(hash);
    }
}
