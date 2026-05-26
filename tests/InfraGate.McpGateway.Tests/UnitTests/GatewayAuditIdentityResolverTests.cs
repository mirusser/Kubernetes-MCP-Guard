using System.Security.Claims;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayAuditIdentityResolverTests
{
    [Fact]
    public void Resolve_NullPrincipal_ReturnsUnauthenticatedIdentity()
    {
        var result = GatewayAuditIdentityResolver.Resolve(null);

        Assert.Null(result.Subject);
        Assert.Null(result.AuthenticationType);
    }

    [Fact]
    public void Resolve_UnauthenticatedPrincipal_ReturnsUnauthenticatedIdentity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Null(result.Subject);
        Assert.Null(result.AuthenticationType);
    }

    [Fact]
    public void Resolve_AuthenticatedWithSubClaim_UsesSubjectAsIdentity()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.Subject, "user-123"),
            new Claim(GatewayAuthConventions.Claims.PreferredUsername, "alice"),
            new Claim(GatewayAuthConventions.Claims.Email, "alice@example.com"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("user-123", result.Subject);
        Assert.Equal(GatewayAuthConventions.Audit.OAuthAuthenticationType, result.AuthenticationType);
    }

    [Fact]
    public void Resolve_AuthenticatedWithoutSubButWithClientId_UsesClientIdAsIdentity()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.ClientId, "my-client"),
            new Claim(GatewayAuthConventions.Claims.PreferredUsername, "alice"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("my-client", result.Subject);
    }

    [Fact]
    public void Resolve_AuthenticatedWithPreferredUsername_FallsBackToPreferredUsernameWhenNoSubOrClientId()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.PreferredUsername, "alice"),
            new Claim(GatewayAuthConventions.Claims.Email, "alice@example.com"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("alice", result.Subject);
    }

    [Fact]
    public void Resolve_AuthenticatedWithOnlyEmail_UsesEmailAsIdentity()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.Email, "alice@example.com"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("alice@example.com", result.Subject);
    }

    [Fact]
    public void Resolve_AuthenticatedWithNoRecognisedClaims_ReturnsNullSubject()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("custom_claim", "some-value"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Null(result.Subject);
        Assert.Equal(GatewayAuthConventions.Audit.OAuthAuthenticationType, result.AuthenticationType);
    }

    [Fact]
    public void Resolve_ServiceClientWithAzpClaim_FormatsSubjectAndIdentifiesAsService()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.AuthorizedParty, GatewayAuthConventions.ServiceClients.ObserverClientId),
            new Claim(GatewayAuthConventions.Claims.Subject, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new Claim(GatewayAuthConventions.Claims.ClientId, "infra-gate-observer"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("service:infra-gate-observer", result.Subject);
        Assert.Equal(GatewayAuthConventions.Audit.ServiceIdentityKind, result.IdentityKind);
    }

    [Theory]
    [InlineData("infra-gate-planner", "service:planner")]
    [InlineData("infra-gate-executor", "service:executor")]
    public void Resolve_RemediationServiceClientWithAzpClaim_UsesConfiguredServiceSubject(
        string clientId,
        string expectedSubject)
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.AuthorizedParty, clientId),
            new Claim(GatewayAuthConventions.Claims.Subject, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new Claim(GatewayAuthConventions.Claims.ClientId, clientId));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal(expectedSubject, result.Subject);
        Assert.Equal(GatewayAuthConventions.Audit.ServiceIdentityKind, result.IdentityKind);
    }

    [Fact]
    public void Resolve_HumanTokenWithoutAzp_IdentifiesAsHuman()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.Subject, "user-123"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal(GatewayAuthConventions.Audit.HumanIdentityKind, result.IdentityKind);
    }

    [Fact]
    public void Resolve_UnknownAzpValue_IdentifiesAsHuman()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.AuthorizedParty, "some-unknown-client"),
            new Claim(GatewayAuthConventions.Claims.Subject, "unknown"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal(GatewayAuthConventions.Audit.HumanIdentityKind, result.IdentityKind);
    }

    [Fact]
    public void Resolve_SubClaimTakesPriorityOverClientId()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.Subject, "sub-value"),
            new Claim(GatewayAuthConventions.Claims.ClientId, "client-value"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("sub-value", result.Subject);
    }

    [Fact]
    public void Resolve_ClientIdTakesPriorityOverPreferredUsername()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(GatewayAuthConventions.Claims.ClientId, "client-value"),
            new Claim(GatewayAuthConventions.Claims.PreferredUsername, "alice"));

        var result = GatewayAuditIdentityResolver.Resolve(principal);

        Assert.Equal("client-value", result.Subject);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
