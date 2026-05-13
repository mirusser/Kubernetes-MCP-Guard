using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InfraGate.Approvals;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Keycloak;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.KeycloakTests.IntegrationTests;

[Trait("Category", "Keycloak")]
public sealed class KeycloakIntegrationTests : IAsyncLifetime
{
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.2";
    private const string RealmName = "infra-gate";
    private const string RealmJsonFileName = "infra-gate-realm.json";
    private const string McpClientId = "mcp-client";
    private const string LimitedClientId = "mcp-client-limited";
    private const string DemoUsername = "demo";
    private const string DemoPassword = "demo";
    private const string Resource = GatewayAuthConventions.DefaultOAuthResource;
    private const string Scope = GatewayAuthConventions.DefaultOAuthScope;

    private KeycloakContainer? keycloakContainer;
    private string keycloakBaseAddress = string.Empty;

    public async Task InitializeAsync()
    {
        string realmJsonPath = Path.Combine(AppContext.BaseDirectory, "TestData", RealmJsonFileName);

        keycloakContainer = new KeycloakBuilder(KeycloakImage)
            .WithRealm(realmJsonPath)
            .Build();

        await keycloakContainer.StartAsync();
        keycloakBaseAddress = keycloakContainer.GetBaseAddress();
    }

    public async Task DisposeAsync()
    {
        if (keycloakContainer is not null)
        {
            await keycloakContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task ValidToken_FromKeycloak_AllowsToolCall()
    {
        using var server = CreateGatewayServer(authority: RealmAuthority());
        using var client = server.CreateClient();

        string token = await AcquireTokenAsync(McpClientId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithWrongAudience_Rejects()
    {
        const string differentResource = "http://127.0.0.1:9999/different-resource";
        using var server = CreateGatewayServer(authority: RealmAuthority(), oauthResource: differentResource);
        using var client = server.CreateClient();

        // Token has aud=http://127.0.0.1:3001/mcp; gateway expects differentResource.
        string token = await AcquireTokenAsync(McpClientId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutScope_Rejects()
    {
        using var server = CreateGatewayServer(authority: RealmAuthority());
        using var client = server.CreateClient();

        // mcp-client-limited has no mcp:tools in default scopes.
        string token = await AcquireTokenAsync(LimitedClientId, requestedScope: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private string RealmAuthority() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}";

    private string TokenEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}/protocol/openid-connect/token";

    private async Task<string> AcquireTokenAsync(string clientId, string? requestedScope = Scope)
    {
        using var http = new HttpClient();
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("client_id", clientId),
            new("username", DemoUsername),
            new("password", DemoPassword)
        };

        if (!string.IsNullOrWhiteSpace(requestedScope))
        {
            formValues.Add(new("scope", requestedScope));
        }

        using var content = new FormUrlEncodedContent(formValues);

        var response = await http.PostAsync(TokenEndpoint(), content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("Token response did not contain access_token.");
    }

    private static TestServer CreateGatewayServer(
        string authority,
        string oauthResource = Resource)
    {
        var authOptions = new GatewayAuthOptions(
            OAuthAuthority: authority,
            OAuthResource: oauthResource,
            OAuthScope: Scope,
            OAuthRequireHttpsMetadata: false,
            OAuthMetadataAddress: null,
            ApprovalOAuthClientId: GatewayAuthConventions.DefaultApprovalOAuthClientId,
            ApprovalOAuthAuthorizationEndpoint: $"{authority}/protocol/openid-connect/auth",
            ApprovalOAuthTokenEndpoint: $"{authority}/protocol/openid-connect/token");

        var options = new McpGatewayOptions(
            authOptions,
            DownstreamProject: "unused",
            GuardAuditRoot: Path.Combine(Path.GetTempPath(), "kc-test-guardrails"),
            WorkingDirectory: Directory.GetCurrentDirectory(),
            ApprovalRoot: Path.Combine(Path.GetTempPath(), "kc-test-approvals"),
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddSingleton<IGuardrailAuditStore, NullAuditStore>();
                services.AddSingleton<IDownstreamMcpClient, NullDownstreamClient>();
                services.AddSingleton<GuardedToolRunner>();
                services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
                services.AddSingleton<ApprovalStore>();
                services.AddSingleton<ApprovalChallengeStore>();
                services.AddSingleton<GatewayApprovalService>();
                services.AddHttpContextAccessor();
                services.AddAntiforgery();
                services.AddGatewayAuthentication(options.Auth);
                services
                    .AddMcpServer()
                    .WithHttpTransport()
                    .WithToolsFromAssembly(typeof(K8sGatewayTools).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGatewayApprovalEndpoints();
                    endpoints.MapMcp(McpGatewayConventions.McpPath)
                        .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);
                });
            }));
    }

    private sealed class NullAuditStore : IGuardrailAuditStore
    {
        public Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NullDownstreamClient : IDownstreamMcpClient
    {
        public Task<string> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult("{}");
    }
}
