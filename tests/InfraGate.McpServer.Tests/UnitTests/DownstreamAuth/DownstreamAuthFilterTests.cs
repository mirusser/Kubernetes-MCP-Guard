using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using InfraGate.DownstreamAuth;
using InfraGate.McpServer.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Tests for DownstreamAuthFilter covering: Required=false pass-through, token absent when
/// Required=true, and valid token accepted when Required=true.
/// </summary>
public sealed class DownstreamAuthFilterTests : IDisposable
{
    private const string TestIssuer = "https://auth.example.com/realms/test";
    private const string TestAudience = "urn:infra-gate:mcp-server";
    private const string TestScope = "mcp:downstream";
    private const string TestGatewayClientId = "infra-gate-gateway";

    private readonly RsaSecurityKey signingKey;

    public DownstreamAuthFilterTests()
    {
        signingKey = new RsaSecurityKey(RSA.Create(2048));
    }

    public void Dispose()
    {
        signingKey.Rsa?.Dispose();
    }
    private static RequestContext<CallToolRequestParams> MakeCallToolRequest(
        IServiceProvider? services = null,
        System.Text.Json.Nodes.JsonObject? meta = null)
    {
        var request = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        request.Params = new CallToolRequestParams { Name = "test_tool", Meta = meta };
        request.Services = services;
        return request;
    }

    private static RequestContext<ListToolsRequestParams> MakeListToolsRequest(IServiceProvider? services = null)
    {
        var request = (RequestContext<ListToolsRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<ListToolsRequestParams>));
        request.Params = new ListToolsRequestParams();
        request.Services = services;
        return request;
    }

    /// <summary>
    /// When no DownstreamTokenValidator is registered in DI (Required=false scenario),
    /// the filter must pass the request through without throwing.
    /// This is the regression test for the GetRequiredService → GetService fix.
    /// </summary>
    [Fact]
    public async Task CallTool_NoValidatorInDi_PassesThroughWithoutThrowing()
    {
        // Arrange: DI container with NO DownstreamTokenValidator registered
        var services = new ServiceCollection().BuildServiceProvider();
        var request = MakeCallToolRequest(services);

        var expectedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }]
        };

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: request passed through, no exception
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ListTools_NoValidatorInDi_PassesThroughWithoutThrowing()
    {
        // Arrange: DI container with NO DownstreamTokenValidator registered
        var services = new ServiceCollection().BuildServiceProvider();
        var request = MakeListToolsRequest(services);

        var expectedResult = new ListToolsResult { Tools = [] };

        var filter = DownstreamAuthFilter.ListTools();
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next =
            (_, _) => new ValueTask<ListToolsResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: request passed through, no exception
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task CallTool_NullServices_PassesThroughWithoutThrowing()
    {
        // Arrange: Services is null (no DI scope at all)
        var request = MakeCallToolRequest(services: null);

        var expectedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }]
        };

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: null Services treated as no validator — pass through
        Assert.Equal(expectedResult, result);
    }

    /// <summary>
    /// Verify that the error code constant is the value that the filter uses in exceptions.
    /// The filter formats errors as "{ErrorCode}: {reason}". This test confirms the constant
    /// is correctly placed and matches the expected value for Task 7 (gateway retry detection).
    /// </summary>
    [Fact]
    public void ErrorCodeConstant_HasExpectedValue()
    {
        // This constant is used by the filter in the McpException message
        // and will be referenced by gateway retry detection logic (Task 7).
        Assert.Equal("downstream_auth_required", DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired);
    }

    /// <summary>
    /// When auth is required and a valid signed token arrives in _meta, the filter must let
    /// the request through.  This is acceptance criterion 2 at the filter level.
    /// </summary>
    [Fact]
    public async Task CallTool_ValidTokenInMeta_PassesThroughToNext()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
            GatewayClientId = TestGatewayClientId,
        };
        var validator = new DownstreamTokenValidator(
            options,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: [signingKey]);

        string token = CreateToken(TestIssuer, TestAudience, TestScope, TestGatewayClientId, signingKey);
        var meta = new System.Text.Json.Nodes.JsonObject
        {
            [DownstreamAuthConventions.MetaKey] = DownstreamAuthConventions.BearerPrefix + token
        };

        var services = new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();

        var request = MakeCallToolRequest(services, meta);

        var expectedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }]
        };

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(expectedResult);
        var handler = filter(next);

        var result = await handler(request, CancellationToken.None);

        Assert.Equal(expectedResult, result);
    }

    /// <summary>
    /// When auth is required and no _meta token is present, the filter must throw McpException
    /// with the downstream_auth_required error code.  This is acceptance criterion 4 at the
    /// filter level: direct server stdio without token is refused.
    /// </summary>
    [Fact]
    public async Task CallTool_ValidatorRegisteredButNoToken_ThrowsMcpException()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
        };
        var validator = new DownstreamTokenValidator(
            options,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: [signingKey]);

        var services = new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();

        // No _meta — token absent
        var request = MakeCallToolRequest(services, meta: null);

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "should not reach here" }]
            });
        var handler = filter(next);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => handler(request, CancellationToken.None).AsTask());

        Assert.Contains(DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// When auth is required and no _meta token is present on a listTools request, the filter
    /// must throw McpException with the downstream_auth_required error code.
    /// </summary>
    [Fact]
    public async Task ListTools_ValidatorRegisteredButNoToken_ThrowsMcpException()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
        };
        var validator = new DownstreamTokenValidator(
            options,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: [signingKey]);

        var services = new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();

        var request = MakeListToolsRequest(services);

        var filter = DownstreamAuthFilter.ListTools();
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next =
            (_, _) => new ValueTask<ListToolsResult>(new ListToolsResult { Tools = [] });
        var handler = filter(next);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => handler(request, CancellationToken.None).AsTask());

        Assert.Contains(DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired, ex.Message, StringComparison.Ordinal);
    }

    private static string CreateToken(
        string issuer,
        string audience,
        string scope,
        string clientId,
        RsaSecurityKey key)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new("scope", scope),
            new("azp", clientId),
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
