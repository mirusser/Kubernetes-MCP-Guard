using System.Security.Claims;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ToolScopeGuardTests
{
    [Fact]
    public async Task RequireAnyScopeAsync_NoHttpContext_ReturnsError()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var guard = new ToolScopeGuard(accessor, auditStore, NullLogger<ToolScopeGuard>.Instance);

        var result = await guard.RequireAnyScopeAsync("test-tool", "scope-a");

        Assert.NotNull(result);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RequireAnyScopeAsync_NoUser_ReturnsError()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var guard = new ToolScopeGuard(accessor, auditStore, NullLogger<ToolScopeGuard>.Instance);

        var result = await guard.RequireAnyScopeAsync("test-tool", "scope-a");

        Assert.NotNull(result);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RequireAnyScopeAsync_UserLacksScope_ReturnsDeniedError()
    {
        var accessor = new HttpContextAccessor();
        SetUser(accessor, "test-user", "different-scope");
        var guard = new ToolScopeGuard(accessor, auditStore, NullLogger<ToolScopeGuard>.Instance);

        var result = await guard.RequireAnyScopeAsync("test-tool", "required-scope");

        Assert.NotNull(result);
        Assert.True(result.IsError);
        Assert.Equal(1, auditStore.EventsWritten);
    }

    [Fact]
    public async Task RequireAnyScopeAsync_UserHasScope_ReturnsNull()
    {
        var accessor = new HttpContextAccessor();
        SetUser(accessor, "test-user", "required-scope");
        var guard = new ToolScopeGuard(accessor, auditStore, NullLogger<ToolScopeGuard>.Instance);

        var result = await guard.RequireAnyScopeAsync("test-tool", "required-scope");

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireAnyScopeAsync_UserHasAnyOfMultipleScopes_ReturnsNull()
    {
        var accessor = new HttpContextAccessor();
        SetUser(accessor, "test-user", "scope-b");
        var guard = new ToolScopeGuard(accessor, auditStore, NullLogger<ToolScopeGuard>.Instance);

        var result = await guard.RequireAnyScopeAsync("test-tool", "scope-a", "scope-b");

        Assert.Null(result);
    }

    [Fact]
    public void ToolScopeRequirements_HasReadWriteScopeConstants()
    {
        Assert.Equal("mcp:tools.read", McpGatewayConventions.ToolScopeRequirements.ReadScope);
        Assert.Equal("mcp:tools.write", McpGatewayConventions.ToolScopeRequirements.WriteScope);
    }

    [Fact]
    public void GatewayAuthConventions_HasReadWriteScopeConstants()
    {
        Assert.Equal("mcp:tools.read", GatewayAuthConventions.DefaultReadToolsOAuthScope);
        Assert.Equal("mcp:tools.write", GatewayAuthConventions.DefaultWriteToolsOAuthScope);
    }

    [Fact]
    public void GatewayAuthentication_AcceptedScopes_IncludesReadWriteScopes()
    {
        Assert.Contains(GatewayAuthentication.AcceptedScopes, s => s == "mcp:tools.read");
        Assert.Contains(GatewayAuthentication.AcceptedScopes, s => s == "mcp:tools.write");
    }

    private readonly InMemoryGuardrailAuditStore auditStore = new();

    private static void SetUser(HttpContextAccessor accessor, string subject, params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(GatewayAuthConventions.Claims.Subject, subject)
        };
        claims.AddRange(scopes.Select(scope => new Claim(GatewayAuthConventions.Claims.Scope, scope)));

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
    }

    private sealed class InMemoryGuardrailAuditStore : IGuardrailAuditStore
    {
        public int EventsWritten { get; private set; }

        public Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            EventsWritten++;
            return Task.CompletedTask;
        }
    }
}
