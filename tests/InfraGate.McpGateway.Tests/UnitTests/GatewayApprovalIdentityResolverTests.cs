using System.Security.Claims;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayApprovalIdentityResolverTests
{
    private const string AuthenticationType = "test";
    private const string Subject = "user-123";
    private const string ClientId = "client-123";
    private const string PreferredUsername = "sre@example.com";
    private const string Email = "fallback@example.com";

    [Fact]
    public void Resolve_NullPrincipal_ReturnsNull()
    {
        var identity = GatewayApprovalIdentityResolver.Resolve(null);

        Assert.Null(identity);
    }

    [Fact]
    public void Resolve_UnauthenticatedPrincipal_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(GatewayAuthConventions.Claims.Subject, Subject)
        ]));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.Null(identity);
    }

    [Fact]
    public void Resolve_AuthenticatedPrincipalWithoutSubjectOrClientId_ReturnsNull()
    {
        var principal = CreatePrincipal(new Claim(GatewayAuthConventions.Claims.PreferredUsername, PreferredUsername));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.Null(identity);
    }

    [Fact]
    public void Resolve_SubjectClaim_ReturnsSubjectIdentity()
    {
        var principal = CreatePrincipal(new Claim(GatewayAuthConventions.Claims.Subject, Subject));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(Subject, identity.Subject);
    }

    [Fact]
    public void Resolve_ClientIdWithoutSubject_ReturnsIdentity()
    {
        var principal = CreatePrincipal(new Claim(GatewayAuthConventions.Claims.ClientId, ClientId));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(ClientId, identity.Subject);
        Assert.Equal(ClientId, identity.DisplayName);
    }

    [Fact]
    public void Resolve_PreferredUsernameClaim_ReturnsDisplayName()
    {
        var principal = CreatePrincipal(
            new Claim(GatewayAuthConventions.Claims.Subject, Subject),
            new Claim(GatewayAuthConventions.Claims.PreferredUsername, PreferredUsername),
            new Claim(GatewayAuthConventions.Claims.Email, Email));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(PreferredUsername, identity.DisplayName);
    }

    [Fact]
    public void Resolve_EmailWithoutPreferredUsername_ReturnsDisplayName()
    {
        var principal = CreatePrincipal(
            new Claim(GatewayAuthConventions.Claims.Subject, Subject),
            new Claim(GatewayAuthConventions.Claims.Email, Email));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(Email, identity.DisplayName);
    }

    [Fact]
    public void Resolve_SubjectWithoutDisplayClaims_ReturnsSubjectDisplayName()
    {
        var principal = CreatePrincipal(new Claim(GatewayAuthConventions.Claims.Subject, Subject));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(Subject, identity.DisplayName);
    }

    [Fact]
    public void Resolve_AuthenticatedIdentity_ReturnsAuthenticationType()
    {
        var principal = CreatePrincipal(new Claim(GatewayAuthConventions.Claims.Subject, Subject));

        var identity = GatewayApprovalIdentityResolver.Resolve(principal);

        Assert.NotNull(identity);
        Assert.Equal(AuthenticationType, identity.AuthenticationType);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, AuthenticationType));
}
