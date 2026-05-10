using InfraGate.DevIssuer;

namespace InfraGate.DevIssuer.Tests.UnitTests;

public sealed class DevIssuerStoreTests
{
    private static DevIssuerOptions DefaultOptions => new(
        Issuer: "https://issuer.example.com",
        Resource: "https://resource.example.com",
        Scope: "mcp",
        Subject: "dev-user",
        ApprovalClientId: "approval-client",
        ApprovalRedirectUri: "https://gateway.example.com/approve");

    [Fact]
    public void RegisterClient_ReturnsClientWithGeneratedId()
    {
        var store = new DevIssuerStore(DefaultOptions);

        var client = store.RegisterClient(["https://callback.example.com"], "Test Client");

        Assert.NotEmpty(client.ClientId);
        Assert.Equal("Test Client", client.ClientName);
        Assert.Contains("https://callback.example.com", client.RedirectUris);
    }

    [Fact]
    public void RegisterClient_StoresMultipleClientsIndependently()
    {
        var store = new DevIssuerStore(DefaultOptions);

        var first = store.RegisterClient(["https://a.example.com"], "Client A");
        var second = store.RegisterClient(["https://b.example.com"], "Client B");

        Assert.NotEqual(first.ClientId, second.ClientId);
        Assert.True(store.ClientAllowsRedirectUri(first.ClientId, "https://a.example.com"));
        Assert.True(store.ClientAllowsRedirectUri(second.ClientId, "https://b.example.com"));
        Assert.False(store.ClientAllowsRedirectUri(first.ClientId, "https://b.example.com"));
    }

    [Fact]
    public void ClientAllowsRedirectUri_ReturnsTrueForRegisteredUri()
    {
        var store = new DevIssuerStore(DefaultOptions);
        var client = store.RegisterClient(["https://callback.example.com"], null);

        Assert.True(store.ClientAllowsRedirectUri(client.ClientId, "https://callback.example.com"));
    }

    [Fact]
    public void ClientAllowsRedirectUri_ReturnsFalseForUnregisteredUri()
    {
        var store = new DevIssuerStore(DefaultOptions);
        var client = store.RegisterClient(["https://callback.example.com"], null);

        Assert.False(store.ClientAllowsRedirectUri(client.ClientId, "https://other.example.com"));
    }

    [Fact]
    public void ClientAllowsRedirectUri_ReturnsFalseForUnknownClientId()
    {
        var store = new DevIssuerStore(DefaultOptions);

        Assert.False(store.ClientAllowsRedirectUri("nonexistent-client-id", "https://callback.example.com"));
    }

    [Fact]
    public void CreateAuthorizationCode_StoresCodeRetrievableByTryConsume()
    {
        var store = new DevIssuerStore(DefaultOptions);
        var client = store.RegisterClient(["https://callback.example.com"], null);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var code = store.CreateAuthorizationCode(
            client.ClientId,
            "https://callback.example.com",
            "challenge-abc",
            "https://resource.example.com",
            "mcp",
            expiresAt);

        Assert.NotEmpty(code.Code);
        Assert.Equal(client.ClientId, code.ClientId);
        Assert.Equal("challenge-abc", code.CodeChallenge);

        var found = store.TryConsumeAuthorizationCode(code.Code, _ => true, out var consumed);
        Assert.True(found);
        Assert.NotNull(consumed);
        Assert.Equal(code.Code, consumed.Code);
    }

    [Fact]
    public void TryConsumeAuthorizationCode_ReturnsTrueAndRemovesCode()
    {
        var store = new DevIssuerStore(DefaultOptions);
        var client = store.RegisterClient(["https://callback.example.com"], null);
        var code = store.CreateAuthorizationCode(
            client.ClientId,
            "https://callback.example.com",
            "challenge-xyz",
            "https://resource.example.com",
            "mcp",
            DateTimeOffset.UtcNow.AddMinutes(5));

        var firstAttempt = store.TryConsumeAuthorizationCode(code.Code, _ => true, out _);
        var secondAttempt = store.TryConsumeAuthorizationCode(code.Code, _ => true, out _);

        Assert.True(firstAttempt);
        Assert.False(secondAttempt);
    }

    [Fact]
    public void TryConsumeAuthorizationCode_ReturnsFalseForUnknownCode()
    {
        var store = new DevIssuerStore(DefaultOptions);

        var found = store.TryConsumeAuthorizationCode("nonexistent-code", _ => true, out var consumed);

        Assert.False(found);
        Assert.Null(consumed);
    }

    [Fact]
    public void TryConsumeAuthorizationCode_ReturnsFalseWhenValidatorRejects()
    {
        var store = new DevIssuerStore(DefaultOptions);
        var client = store.RegisterClient(["https://callback.example.com"], null);
        var code = store.CreateAuthorizationCode(
            client.ClientId,
            "https://callback.example.com",
            "challenge-abc",
            "https://resource.example.com",
            "mcp",
            DateTimeOffset.UtcNow.AddMinutes(5));

        var found = store.TryConsumeAuthorizationCode(code.Code, _ => false, out var consumed);

        Assert.False(found);
        Assert.Null(consumed);
    }
}
