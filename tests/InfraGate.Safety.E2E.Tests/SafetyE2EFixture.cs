using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Testcontainers.Keycloak;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.Safety.E2E.Tests;

public sealed partial class SafetyE2EFixture : IAsyncLifetime
{
    public const string EnableEnvVar = "INFRA_GATE_RUN_SAFETY_E2E";
    public const string KubeconfigEnvVar = "KUBECONFIG";

    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.6.1";
    private const string RealmName = "infra-gate";
    private const string RealmJsonFileName = "infra-gate-realm.json";
    private const string McpClientId = "mcp-smoke-client";
    private const string DemoUsername = "demo";
    private const string DemoPassword = "demo";
    private const string DefaultNamespace = "mcp-nginx-demo";
    private const string KubeconfigRelativePath = ".kube/mcp-nginx-demo.config";

    private readonly string repoRoot;
    private readonly string approvalRoot;
    private readonly string guardAuditRoot;
    private readonly string namespaceName;
    private readonly string kubeconfigPath;

    private KeycloakContainer? keycloakContainer;
    private TestServer? gatewayServer;
    private DownstreamMcpClient? downstreamClient;
    private ApprovalStore? approvalStore;
    private ApprovalChallengeStore? challengeStore;
    private string keycloakBaseAddress = string.Empty;
    private string approvalOAuthSubject = DemoUsername;

    public SafetyE2EFixture()
    {
        repoRoot = FindRepoRoot();
        approvalRoot = Path.Combine(Path.GetTempPath(), "infra-gate-safety-e2e", Guid.NewGuid().ToString("N"));
        guardAuditRoot = Path.Combine(approvalRoot, "guardrails");
        namespaceName = Environment.GetEnvironmentVariable("K8S_MCP_ALLOWED_NAMESPACES") ?? DefaultNamespace;
        kubeconfigPath = ResolveKubeconfigPath(repoRoot);
    }

    public bool IsEnabled { get; private set; }

    public string ApprovalRoot => approvalRoot;

    public string Namespace => namespaceName;

    public ApprovalStore ApprovalStore =>
        approvalStore ?? throw new InvalidOperationException("Fixture is not initialised.");

    public ApprovalChallengeStore ChallengeStore =>
        challengeStore ?? throw new InvalidOperationException("Fixture is not initialised.");

    public IDownstreamMcpClient DownstreamClient =>
        downstreamClient ?? throw new InvalidOperationException("Fixture is not initialised.");

    public string RealmAuthority =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}";

    public string TokenEndpoint =>
        $"{RealmAuthority}/protocol/openid-connect/token";

    public async Task InitializeAsync()
    {
        IsEnabled = Environment.GetEnvironmentVariable(EnableEnvVar) == "1";
        if (!IsEnabled)
        {
            return;
        }

        Directory.CreateDirectory(approvalRoot);
        Directory.CreateDirectory(guardAuditRoot);

        // The downstream McpServer subprocess inherits these env vars when DownstreamMcpClient spawns it.
        Environment.SetEnvironmentVariable(ApprovalConventions.EnvironmentVariables.ApprovalRoot, approvalRoot);
        Environment.SetEnvironmentVariable("K8S_MCP_ALLOWED_NAMESPACES", namespaceName);
        Environment.SetEnvironmentVariable(KubeconfigEnvVar, kubeconfigPath);

        string realmJsonPath = Path.Combine(AppContext.BaseDirectory, "TestData", RealmJsonFileName);
        keycloakContainer = new KeycloakBuilder(KeycloakImage)
            .WithRealm(realmJsonPath)
            .Build();
        await keycloakContainer.StartAsync();
        keycloakBaseAddress = keycloakContainer.GetBaseAddress();

        var storeOptions = new ApprovalStoreOptions(approvalRoot);
        approvalStore = new ApprovalStore(storeOptions);
        challengeStore = new ApprovalChallengeStore(storeOptions);

        gatewayServer = CreateGatewayServer();
        downstreamClient = gatewayServer.Services.GetRequiredService<IDownstreamMcpClient>() as DownstreamMcpClient
            ?? throw new InvalidOperationException("Gateway did not register the real DownstreamMcpClient.");
    }

    public async Task DisposeAsync()
    {
        if (downstreamClient is not null)
        {
            await downstreamClient.DisposeAsync();
        }

        gatewayServer?.Dispose();

        if (keycloakContainer is not null)
        {
            await keycloakContainer.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(approvalRoot))
            {
                Directory.Delete(approvalRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; ignore if files are still held.
        }
    }

    public HttpClient CreateGatewayHttpClient(string? bearerToken = null)
    {
        if (gatewayServer is null)
        {
            throw new InvalidOperationException("Fixture is not initialised.");
        }

        var client = gatewayServer.CreateClient();
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return client;
    }

    public HttpClient CreateApprovalBrowser()
    {
        if (gatewayServer is null)
        {
            throw new InvalidOperationException("Fixture is not initialised.");
        }

        return new HttpClient(gatewayServer.CreateHandler())
        {
            BaseAddress = gatewayServer.BaseAddress
        };
    }

    public async Task<SafetyHttpMcpClient> CreateHttpMcpClientAsync(CancellationToken cancellationToken = default)
    {
        var token = await AcquireTokenAsync(
            scope: $"openid {GatewayAuthConventions.DefaultOAuthScope}",
            cancellationToken: cancellationToken);
        var httpClient = CreateGatewayHttpClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, McpGatewayConventions.McpPath),
                Name = "infra-gate-safety-e2e",
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}"
                }
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: true);

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return new SafetyHttpMcpClient(client, ReadJwtSubject(token));
    }

    public async Task<string> AcquireTokenAsync(
        string username = DemoUsername,
        string password = DemoPassword,
        string clientId = McpClientId,
        string? scope = GatewayAuthConventions.DefaultOAuthScope,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("client_id", clientId),
            new("username", username),
            new("password", password)
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            formValues.Add(new("scope", scope));
        }

        using var content = new FormUrlEncodedContent(formValues);
        var response = await http.PostAsync(TokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return json.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("Token response did not contain access_token.");
    }

    public IGatewayApprovalService GetApprovalService() =>
        gatewayServer?.Services.GetRequiredService<IGatewayApprovalService>()
        ?? throw new InvalidOperationException("Fixture is not initialised.");

    public async Task<HttpClient> CreateAuthenticatedApprovalBrowserAsync(
        string challengeId,
        string subject,
        CancellationToken cancellationToken = default)
    {
        approvalOAuthSubject = subject;
        var browser = CreateApprovalBrowser();

        var pageRedirect = await browser.GetAsync($"/approvals/{challengeId}", cancellationToken);
        var loginPath = pageRedirect.Headers.Location?.ToString() ??
                        throw new InvalidOperationException("Approval page did not redirect to login.");
        var loginRedirect = await browser.GetAsync(loginPath, cancellationToken);
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

        var callback = await browser.SendAsync(callbackRequest, cancellationToken);
        AddResponseCookies(browser, callback);

        return browser;
    }

    public async Task<string> ApproveChallengeInBrowserAsync(
        string challengeId,
        string subject,
        CancellationToken cancellationToken = default)
    {
        using var browser = await CreateAuthenticatedApprovalBrowserAsync(challengeId, subject, cancellationToken);
        var page = await browser.GetAsync($"/approvals/{challengeId}", cancellationToken);
        page.EnsureSuccessStatusCode();
        var pageText = await page.Content.ReadAsStringAsync(cancellationToken);
        AddResponseCookies(browser, page);

        return await PostApprovalAsync(
            browser,
            challengeId,
            ParseAntiforgeryToken(pageText),
            cancellationToken);
    }

    public static async Task<string> PostApprovalAsync(
        HttpClient browser,
        string challengeId,
        string requestVerificationToken,
        CancellationToken cancellationToken = default)
    {
        var approvalResponse = await browser.PostAsync(
            $"/approvals/{challengeId}/approve",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [McpGatewayConventions.Approvals.RequestVerificationToken] = requestVerificationToken
            }),
            cancellationToken);
        approvalResponse.EnsureSuccessStatusCode();

        return await approvalResponse.Content.ReadAsStringAsync(cancellationToken);
    }

    public static void AddResponseCookies(HttpClient client, HttpResponseMessage response)
    {
        var cookies = CookieHeader(response);
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            var existingCookies = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            var combinedCookies = string.Join(
                "; ",
                new[] { existingCookies, cookies }.Where(value => !string.IsNullOrWhiteSpace(value)));

            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", combinedCookies);
        }
    }

    // Deliberate test shortcut: this method injects a ClaimsPrincipal directly into
    // IHttpContextAccessor rather than acquiring a real JWT from Keycloak and routing
    // through the gateway's HTTP + JWT-bearer pipeline. Endpoint tests should prefer
    // CreateHttpMcpClientAsync and CreateAuthenticatedApprovalBrowserAsync; this
    // helper remains for focused service-level probes.
    //
    // Why this is acceptable: GatewayApprovalService.ApproveChallengeAsync resolves
    // the authenticated subject from IHttpContextAccessor exactly the same way
    // whether the principal was built by JwtBearer middleware or set here, so the
    // same-subject enforcement code path under test is identical.
    //
    // Why it's still a compromise: the OAuth and HTTP layers are NOT exercised for
    // any test that uses this shortcut. SmokeTests covers those layers separately
    // with real Keycloak JWTs, so the project as a whole still proves "real OAuth
    // path" — just not for the wrong-user assertion specifically.
    //
    // If a follow-up wants stricter real-OIDC coverage for approval decisions, add a
    // second user to infra-gate-realm.json and use that user for browser OAuth too.
    public void SetAuthenticatedSubject(string subject)
    {
        var accessor = gatewayServer?.Services.GetRequiredService<IHttpContextAccessor>()
            ?? throw new InvalidOperationException("Fixture is not initialised.");
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GatewayAuthConventions.Claims.Subject, subject),
                new Claim(GatewayAuthConventions.Claims.Scope, GatewayAuthConventions.DefaultOAuthScope)
            ], "test"))
        };
    }

    public void ClearAuthenticatedSubject()
    {
        var accessor = gatewayServer?.Services.GetRequiredService<IHttpContextAccessor>()
            ?? throw new InvalidOperationException("Fixture is not initialised.");
        accessor.HttpContext = null;
    }

    public void SetAuthenticatedFromJwt(string token)
    {
        var subject = ReadJwtSubject(token);
        var accessor = gatewayServer?.Services.GetRequiredService<IHttpContextAccessor>()
            ?? throw new InvalidOperationException("Fixture is not initialised.");

        var claims = new List<Claim>
        {
            new(GatewayAuthConventions.Claims.Subject, subject)
        };

        var parts = token.Split('.');
        if (parts.Length >= 2)
        {
            using var document = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            foreach (var claimName in new[]
                     {
                         GatewayAuthConventions.Claims.Scope,
                         GatewayAuthConventions.Claims.ClientId,
                         GatewayAuthConventions.Claims.PreferredUsername
                     })
            {
                if (document.RootElement.TryGetProperty(claimName, out var claimValue) &&
                    !string.IsNullOrWhiteSpace(claimValue.GetString()))
                {
                    claims.Add(new Claim(claimName, claimValue.GetString()!));
                }
            }
        }

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "oauth-jwt"))
        };
    }

    // PlanId is always a 32-char lowercase hex string (16 random bytes, hex-encoded).
    // Extracting by format rather than by surrounding text avoids brittleness when response messages change.
    [System.Text.RegularExpressions.GeneratedRegex(@"\b[0-9a-f]{32}\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PlanIdPattern();

    public static string ParsePlanId(string text) =>
        PlanIdPattern().Matches(text) is { Count: > 0 } matches
            ? matches[0].Value
            : throw new InvalidOperationException("Could not extract a PlanId from the text.");

    public static string ParseChallengeId(string text) =>
        ChallengeIdPattern().Match(text) is { Success: true } match
            ? match.Groups["id"].Value
            : throw new InvalidOperationException("Could not extract an approval challenge id from the text.");

    [System.Text.RegularExpressions.GeneratedRegex(@"https?://[^/]+/approvals/(?<id>[0-9a-f]+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ChallengeIdPattern();

    public static string ParseAntiforgeryToken(string html)
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

    public async Task<IReadOnlyList<JsonElement>> ReadAuditEventsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(approvalRoot, ApprovalConventions.Storage.AuditFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        // ApprovalStore.WriteAuditAsync serialises with WriteIndented = true, so each
        // audit record spans multiple physical lines. A line-by-line JSON parse would
        // fail on the first '{'. Utf8JsonReader with AllowMultipleValues iterates the
        // sequence of top-level objects regardless of formatting.
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var events = new List<JsonElement>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                continue;
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            events.Add(doc.RootElement.Clone());
        }

        return events;
    }

    private TestServer CreateGatewayServer()
    {
        var authOptions = new GatewayAuthOptions(
            OAuthAuthority: RealmAuthority,
            OAuthResource: GatewayAuthConventions.DefaultOAuthResource,
            OAuthScope: GatewayAuthConventions.DefaultOAuthScope,
            OAuthRequireHttpsMetadata: false,
            OAuthMetadataAddress: null,
            ApprovalOAuthClientId: GatewayAuthConventions.DefaultApprovalOAuthClientId,
            ApprovalOAuthAuthorizationEndpoint: $"{RealmAuthority}/protocol/openid-connect/auth",
            ApprovalOAuthTokenEndpoint: $"{RealmAuthority}/protocol/openid-connect/token");

        var downstreamProject = Path.Combine(repoRoot, "src", "InfraGate.McpServer", "InfraGate.McpServer.csproj");

        var options = new McpGatewayOptions(
            authOptions,
            DownstreamProject: downstreamProject,
            GuardAuditRoot: guardAuditRoot,
            WorkingDirectory: repoRoot,
            ApprovalRoot: approvalRoot,
            ApprovalBaseUrl: "http://gateway.test",
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl,
            DownstreamAssembly: null,
            RuntimeMode: RuntimeMode.Development);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
                services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
                services.AddSingleton<GuardedToolRunner>();
                services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
                services.AddSingleton<ApprovalStore>();
                services.AddSingleton<IApprovalAuditPublisher, ApprovalStoreAuditPublisher>();
                services.AddSingleton<ApprovalChallengeStore>();
                services.AddSingleton<IApprovalChallengeStore>(sp => sp.GetRequiredService<ApprovalChallengeStore>());
                services.AddSingleton<IAuthorizationCheck, SameSubjectAuthorizationCheck>();
                services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
                services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
                services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
                services.AddKubernetesAdapter();
                services.AddSingleton<DownstreamToolRegistry>();
                services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
                services.AddHttpContextAccessor();
                services.AddLogging();
                services.AddAntiforgery();
                services.AddGatewayAuthentication(options.Auth);
                services.PostConfigure<OAuthOptions>(GatewayAuthConventions.Schemes.ApprovalOAuth, oauthOptions =>
                {
                    oauthOptions.Backchannel = new HttpClient(new FakeApprovalOAuthBackchannel(() => approvalOAuthSubject));
                });

                services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
                services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();

                services
                    .AddMcpServer(serverOptions =>
                    {
                        serverOptions.Capabilities = new ServerCapabilities
                        {
                            Resources = new ResourcesCapability { Subscribe = true }
                        };
                    })
                    .WithHttpTransport()
                    .WithListToolsHandler((RequestContext<ListToolsRequestParams> request, CancellationToken ct) =>
                        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().ListToolsAsync(request.Params, ct)))
                    .WithCallToolHandler((RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
                    {
                        if (request.Services!.GetService<IHttpContextAccessor>() is { HttpContext: { } httpCtx })
                        {
                            httpCtx.Items[NotificationsConventions.McpSessionIdItemKey] = request.Server.SessionId;
                        }
                        return new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().CallToolAsync(request.Params, ct));
                    });
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

    private static string ResolveKubeconfigPath(string repoRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(KubeconfigEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return Path.Combine(repoRoot, KubeconfigRelativePath);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (.git directory not found).");
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(value => value.Split(';', 2)[0]))
            : string.Empty;
    }

    private static string ReadJwtSubject(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("JWT did not contain a payload.");
        }

        using var document = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        foreach (var claimName in new[]
                 {
                     GatewayAuthConventions.Claims.Subject,
                     GatewayAuthConventions.Claims.ClientId
                 })
        {
            if (document.RootElement.TryGetProperty(claimName, out var claim) &&
                !string.IsNullOrWhiteSpace(claim.GetString()))
            {
                return claim.GetString()!;
            }
        }

        var claimNames = string.Join(", ", document.RootElement.EnumerateObject().Select(property => property.Name));
        throw new InvalidOperationException($"JWT did not contain a usable subject. Claims: {claimNames}");
    }

    private static string CreateApprovalJwt(string subject)
    {
        var header = EncodeBase64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = EncodeBase64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            [GatewayAuthConventions.Claims.Subject] = subject,
            [GatewayAuthConventions.Claims.PreferredUsername] = subject,
            [GatewayAuthConventions.Claims.Scope] = GatewayAuthConventions.DefaultOAuthScope
        }));

        return $"{header}.{payload}.";
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

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

    public sealed class SafetyHttpMcpClient(McpClient client, string subject) : IAsyncDisposable
    {
        public string Subject { get; } = subject;

        public async Task<string> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default)
        {
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

            return string.Join(
                Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(content => content.Text));
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }

    private sealed class FakeApprovalOAuthBackchannel(Func<string> subjectProvider) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(new
            {
                access_token = CreateApprovalJwt(subjectProvider()),
                token_type = "Bearer"
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
