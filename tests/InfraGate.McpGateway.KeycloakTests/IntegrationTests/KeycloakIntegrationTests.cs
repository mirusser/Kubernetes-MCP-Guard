using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Keycloak;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.KeycloakTests.IntegrationTests;

[Trait("Category", "Keycloak")]
public sealed class KeycloakIntegrationTests : IAsyncLifetime
{
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.6.1";
    private const string RealmName = "infra-gate";
    private const string RealmJsonFileName = "infra-gate-realm.json";
    private const string SmokeClientId = "mcp-smoke-client";
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

        string token = await AcquireTokenAsync(SmokeClientId);
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
        string token = await AcquireTokenAsync(SmokeClientId);
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

    [Fact]
    public async Task Discovery_ExposesDynamicClientRegistrationEndpoint()
    {
        using var http = new HttpClient();

        using var document = await GetDiscoveryDocumentAsync(http);

        var registrationEndpoint = document.RootElement
            .GetProperty("registration_endpoint")
            .GetString();

        Assert.Equal(RegistrationEndpoint(), registrationEndpoint);
    }

    [Fact]
    public async Task DynamicClientRegistration_PublicLoopbackClient_AllowsRegistration()
    {
        using var http = new HttpClient();

        var response = await RegisterClientAsync(
            http,
            redirectUri: "http://127.0.0.1:4567/callback",
            scope: "openid profile email mcp:tools");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("client_id").GetString()));
        Assert.Equal("none", document.RootElement.GetProperty("token_endpoint_auth_method").GetString());
    }

    [Fact]
    public async Task DynamicClientRegistration_UntrustedRedirectUri_RejectsRegistration()
    {
        using var http = new HttpClient();

        var response = await RegisterClientAsync(
            http,
            redirectUri: "https://evil.example/callback",
            scope: "openid profile email mcp:tools");

        AssertClientRegistrationRejected(response);
    }

    [Fact]
    public async Task DynamicClientRegistration_DisallowedScope_RejectsRegistration()
    {
        using var http = new HttpClient();

        var response = await RegisterClientAsync(
            http,
            redirectUri: "http://127.0.0.1:4568/callback",
            scope: "openid profile email mcp:admin");

        AssertClientRegistrationRejected(response);
    }

    [Fact]
    public async Task KeycloakToken_IncludesGatewayAudienceScopeAndIdentityClaims()
    {
        string token = await AcquireTokenAsync(SmokeClientId);

        using var document = DecodeJwtPayload(token);
        var root = document.RootElement;

        Assert.Contains(Resource, GetAudiences(root));
        Assert.Contains(Scope, root.GetProperty("scope").GetString()!.Split(' '));
        Assert.Equal(DemoUsername, root.GetProperty("sub").GetString());
        Assert.Equal(DemoUsername, root.GetProperty("preferred_username").GetString());
    }

    [Fact]
    public async Task ApprovalBrowser_WithRealKeycloakTokenBackchannel_ApprovesChallenge()
    {
        using var server = CreateGatewayServer(
            authority: RealmAuthority(),
            approvalOAuthBackchannel: new KeycloakTokenBackchannel(TokenEndpoint()));
        var approvalStore = server.Services.GetRequiredService<ApprovalStore>();
        var challengeStore = server.Services.GetRequiredService<ApprovalChallengeStore>();
        var planResult = await approvalStore.CreatePlanAsync(CreateApprovalPlan(), CancellationToken.None);
        var challenge = await challengeStore.CreateAsync(
            planResult.Plan.Id,
            planResult.Hash,
            DemoUsername,
            GatewayAuthConventions.Audit.OAuthAuthenticationType,
            McpGatewayOptions.DefaultApprovalChallengeTtl,
            CancellationToken.None);
        using var browser = await CreateAuthenticatedApprovalBrowserAsync(server, challenge.Id);

        var page = await browser.GetAsync($"/approvals/{challenge.Id}");
        page.EnsureSuccessStatusCode();
        var pageText = await page.Content.ReadAsStringAsync();
        AddResponseCookies(browser, page);
        var approve = await browser.PostAsync(
            $"/approvals/{challenge.Id}/approve",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [McpGatewayConventions.Approvals.RequestVerificationToken] = ParseAntiforgeryToken(pageText)
            }));
        approve.EnsureSuccessStatusCode();
        var approveText = await approve.Content.ReadAsStringAsync();
        var approvedChallenge = await challengeStore.GetAsync(challenge.Id, CancellationToken.None);

        Assert.Contains("Approval Recorded", approveText);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Approved, approvedChallenge?.Status);
        Assert.Equal(DemoUsername, approvedChallenge?.ApproverSubject);
    }

    private string RealmAuthority() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}";

    private string TokenEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}/protocol/openid-connect/token";

    private string RegistrationEndpoint() =>
        $"{RealmAuthority()}/clients-registrations/openid-connect";

    private async Task<JsonDocument> GetDiscoveryDocumentAsync(HttpClient http)
    {
        var response = await http.GetAsync($"{RealmAuthority()}/.well-known/openid-configuration");
        response.EnsureSuccessStatusCode();

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private Task<HttpResponseMessage> RegisterClientAsync(
        HttpClient http,
        string redirectUri,
        string scope)
    {
        var body = new
        {
            client_name = "InfraGate DCR test client",
            redirect_uris = new[] { redirectUri },
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
            scope
        };

        return http.PostAsJsonAsync(RegistrationEndpoint(), body);
    }

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

    private static JsonDocument DecodeJwtPayload(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("JWT did not contain a payload segment.");
        }

        return JsonDocument.Parse(DecodeBase64Url(segments[1]));
    }

    private static string[] GetAudiences(JsonElement root)
    {
        var audience = root.GetProperty("aud");
        return audience.ValueKind switch
        {
            JsonValueKind.Array => audience.EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            JsonValueKind.String => [audience.GetString()!],
            _ => []
        };
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new InvalidOperationException("JWT payload was not valid base64url.")
        };

        return Convert.FromBase64String(padded);
    }

    private static void AssertClientRegistrationRejected(HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"Expected DCR policy rejection, got {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static K8sPlan CreateApprovalPlan()
    {
        var objects = new[] { new K8sObjectRef("apps/v1", "Deployment", "mcp-nginx-demo", "nginx-demo") };

        return new K8sPlan(
            ApprovalStore.NewPlanId(),
            "scale",
            "mcp-nginx-demo",
            DateTimeOffset.UtcNow,
            "Scale nginx-demo deployment.",
            new Dictionary<string, string>
            {
                ["name"] = "nginx-demo",
                ["replicas"] = "2"
            },
            objects)
        {
            DryRun = CreateDryRun(objects),
            Diffs = CreateDiffs(objects)
        };
    }

    private static K8sPlanDryRun CreateDryRun(IReadOnlyList<K8sObjectRef> objects) =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            objects.Select(obj => new K8sPlanDryRunObject(
                $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}",
                "{}")).ToArray(),
            [],
            "Server-side dry-run succeeded.");

    private static K8sPlanDiff[] CreateDiffs(IReadOnlyList<K8sObjectRef> objects) =>
        objects.Select(obj => new K8sPlanDiff(
            obj,
            ApprovalConventions.DiffChangeTypes.Update,
            $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name} will be updated.",
            """
            --- live
            +++ proposed
             spec:
            -  replicas: 1
            +  replicas: 2
            """,
            """{"spec":{"replicas":1}}""",
            """{"spec":{"replicas":2}}""",
            [],
            [],
            ["/spec/replicas"])).ToArray();

    private static async Task<HttpClient> CreateAuthenticatedApprovalBrowserAsync(
        TestServer server,
        string challengeId)
    {
        var browser = new HttpClient(server.CreateHandler())
        {
            BaseAddress = new Uri("http://127.0.0.1:3001")
        };

        var pageRedirect = await browser.GetAsync($"/approvals/{challengeId}");
        var loginPath = pageRedirect.Headers.Location?.ToString() ??
                        throw new InvalidOperationException("Approval page did not redirect to login.");
        var loginRedirect = await browser.GetAsync(loginPath);
        var correlationCookie = CookieHeader(loginRedirect);
        var authorizationUri = loginRedirect.Headers.Location ??
                               throw new InvalidOperationException("Login did not redirect to OAuth authorization.");
        var state = QueryHelpers.ParseQuery(authorizationUri.Query)["state"].ToString();
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new InvalidOperationException("OAuth authorization redirect did not contain state.");
        }

        using var callbackRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{GatewayAuthConventions.Approvals.DefaultCallbackPath}?code=test-code&state={Uri.EscapeDataString(state)}");
        callbackRequest.Headers.Add("Cookie", correlationCookie);

        var callback = await browser.SendAsync(callbackRequest);
        AddResponseCookies(browser, callback);

        return browser;
    }

    private static void AddResponseCookies(HttpClient client, HttpResponseMessage response)
    {
        var cookies = CookieHeader(response);
        if (string.IsNullOrWhiteSpace(cookies))
        {
            return;
        }

        var existingCookies = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
            ? string.Join("; ", values)
            : string.Empty;
        var combinedCookies = string.Join(
            "; ",
            new[] { existingCookies, cookies }.Where(value => !string.IsNullOrWhiteSpace(value)));

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", combinedCookies);
    }

    private static string CookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(value => value.Split(';', 2)[0]))
            : string.Empty;

    private static string ParseAntiforgeryToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" value=\"";

        var valueStart = html.IndexOf(marker, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            throw new InvalidOperationException("Approval page did not contain an antiforgery token.");
        }

        valueStart += marker.Length;
        var valueEnd = html.IndexOf('"', valueStart);
        if (valueEnd < valueStart)
        {
            throw new InvalidOperationException("Approval page contained a malformed antiforgery token.");
        }

        return WebUtility.HtmlDecode(html[valueStart..valueEnd]);
    }

    private static TestServer CreateGatewayServer(
        string authority,
        string oauthResource = Resource,
        HttpMessageHandler? approvalOAuthBackchannel = null)
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

        var root = Path.Combine(Path.GetTempPath(), "kc-test", Guid.NewGuid().ToString("N"));
        var options = new McpGatewayOptions(
            authOptions,
            DownstreamProject: "unused",
            GuardAuditRoot: Path.Combine(root, "guardrails"),
            WorkingDirectory: Directory.GetCurrentDirectory(),
            ApprovalRoot: Path.Combine(root, "approvals"),
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
                if (approvalOAuthBackchannel is not null)
                {
                    services.PostConfigure<OAuthOptions>(
                        GatewayAuthConventions.Schemes.ApprovalOAuth,
                        oauthOptions => oauthOptions.Backchannel = new HttpClient(approvalOAuthBackchannel));
                }

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

    private sealed class KeycloakTokenBackchannel(string tokenEndpoint) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var http = new HttpClient();
            using var content = new FormUrlEncodedContent(
            [
                new("grant_type", "password"),
                new("client_id", SmokeClientId),
                new("username", DemoUsername),
                new("password", DemoPassword),
                new("scope", Scope)
            ]);
            var tokenResponse = await http.PostAsync(tokenEndpoint, content, cancellationToken);
            tokenResponse.EnsureSuccessStatusCode();
            using var tokenDocument = await JsonDocument.ParseAsync(
                await tokenResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString()
                              ?? throw new InvalidOperationException("Keycloak token response did not contain access_token.");
            var json = JsonSerializer.Serialize(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 300,
                scope = Scope
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
