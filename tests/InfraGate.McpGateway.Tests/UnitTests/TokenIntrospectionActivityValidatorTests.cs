using InfraGate.McpGateway.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class TokenIntrospectionActivityValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsActiveAsync_ActiveToken_CachesUntilConfiguredTtl()
    {
        var timeProvider = new ManualTimeProvider(Start);
        var introspectionClient = new SequenceIntrospectionClient(TokenIntrospectionResult.Active(Start.AddMinutes(5)));
        var validator = CreateValidator(introspectionClient, timeProvider, cacheSeconds: 30);
        var token = CreateToken(Start, Start.AddMinutes(5));

        bool first = await validator.IsActiveAsync(token, CancellationToken.None);
        bool second = await validator.IsActiveAsync(token, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, introspectionClient.CallCount);
    }

    [Fact]
    public async Task IsActiveAsync_CacheExpires_ReintrospectsAndRejectsInactiveToken()
    {
        var timeProvider = new ManualTimeProvider(Start);
        var introspectionClient = new SequenceIntrospectionClient(
            TokenIntrospectionResult.Active(Start.AddMinutes(5)),
            TokenIntrospectionResult.Inactive);
        var validator = CreateValidator(introspectionClient, timeProvider, cacheSeconds: 30);
        var token = CreateToken(Start, Start.AddMinutes(5));

        bool first = await validator.IsActiveAsync(token, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        bool second = await validator.IsActiveAsync(token, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2, introspectionClient.CallCount);
    }

    [Fact]
    public async Task IsActiveAsync_CacheDoesNotOutliveTokenExpiration()
    {
        var timeProvider = new ManualTimeProvider(Start);
        var introspectionClient = new SequenceIntrospectionClient(
            TokenIntrospectionResult.Active(Start.AddSeconds(10)),
            TokenIntrospectionResult.Active(Start.AddMinutes(5)));
        var validator = CreateValidator(introspectionClient, timeProvider, cacheSeconds: 30);
        var token = CreateToken(Start, Start.AddSeconds(10));

        bool first = await validator.IsActiveAsync(token, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(11));
        bool second = await validator.IsActiveAsync(token, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(2, introspectionClient.CallCount);
    }

    [Fact]
    public async Task IsActiveAsync_InactiveResult_IsNotCachedAsSuccessfulAuthorization()
    {
        var timeProvider = new ManualTimeProvider(Start);
        var introspectionClient = new SequenceIntrospectionClient(
            TokenIntrospectionResult.Inactive,
            TokenIntrospectionResult.Active(Start.AddMinutes(5)));
        var validator = CreateValidator(introspectionClient, timeProvider, cacheSeconds: 30);
        var token = CreateToken(Start, Start.AddMinutes(5));

        bool first = await validator.IsActiveAsync(token, CancellationToken.None);
        bool second = await validator.IsActiveAsync(token, CancellationToken.None);

        Assert.False(first);
        Assert.True(second);
        Assert.Equal(2, introspectionClient.CallCount);
    }

    [Fact]
    public async Task PruneExpiredEntries_RemovesOnlyExpiredEntries()
    {
        var timeProvider = new ManualTimeProvider(Start);
        var introspectionClient = new SequenceIntrospectionClient(
            TokenIntrospectionResult.Active(Start.AddMinutes(5)),
            TokenIntrospectionResult.Active(Start.AddMinutes(5)));
        var validator = CreateValidator(introspectionClient, timeProvider, cacheSeconds: 30);
        var shortLivedToken = CreateToken(Start, Start.AddSeconds(10));
        var longLivedToken = CreateToken(Start, Start.AddMinutes(5));

        await validator.IsActiveAsync(shortLivedToken, CancellationToken.None);
        await validator.IsActiveAsync(longLivedToken, CancellationToken.None);
        Assert.Equal(2, introspectionClient.CallCount);

        timeProvider.Advance(TimeSpan.FromSeconds(11));
        validator.PruneExpiredEntries();

        await validator.IsActiveAsync(longLivedToken, CancellationToken.None);
        Assert.Equal(2, introspectionClient.CallCount);

        await validator.IsActiveAsync(shortLivedToken, CancellationToken.None);
        Assert.Equal(3, introspectionClient.CallCount);
    }

    private static TokenIntrospectionActivityValidator CreateValidator(
        ITokenIntrospectionClient introspectionClient,
        TimeProvider timeProvider,
        int cacheSeconds)
    {
        var options = new GatewayAuthOptions(
            "https://issuer.example.com",
            TokenIntrospectionCacheSeconds: cacheSeconds);
        return new TokenIntrospectionActivityValidator(introspectionClient, options, timeProvider);
    }

    private static JsonWebToken CreateToken(DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray());
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "https://issuer.example.com",
            Audience = GatewayAuthConventions.DefaultOAuthResource,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebToken(new JsonWebTokenHandler().CreateToken(descriptor));
    }

    private sealed class SequenceIntrospectionClient(params TokenIntrospectionResult[] results) : ITokenIntrospectionClient
    {
        public int CallCount { get; private set; }

        public Task<TokenIntrospectionResult> IntrospectAsync(
            JsonWebToken accessToken,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(accessToken);
            var index = Math.Min(CallCount, results.Length - 1);
            CallCount++;
            return Task.FromResult(results[index]);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset currentTime = currentTime;

        public override DateTimeOffset GetUtcNow() => currentTime;

        public void Advance(TimeSpan delay)
        {
            currentTime = currentTime.Add(delay);
        }
    }
}
