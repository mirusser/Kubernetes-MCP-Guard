using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Keycloak;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.Safety.E2E.Tests;

public sealed class SafetyE2EFixture : IAsyncLifetime
{
    public const string EnableEnvVar = "INFRA_GATE_RUN_SAFETY_E2E";
    public const string KubeconfigEnvVar = "KUBECONFIG";

    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.2";
    private const string RealmName = "infra-gate";
    private const string RealmJsonFileName = "infra-gate-realm.json";
    private const string McpClientId = "mcp-client";
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

    public GatewayApprovalService GetApprovalService() =>
        gatewayServer?.Services.GetRequiredService<GatewayApprovalService>()
        ?? throw new InvalidOperationException("Fixture is not initialised.");

    // Deliberate test shortcut: this method injects a ClaimsPrincipal directly into
    // IHttpContextAccessor rather than acquiring a real JWT from Keycloak and routing
    // through the gateway's HTTP + JWT-bearer pipeline. It exists so a single fixture
    // can simulate two distinct authenticated subjects without:
    //   - adding a second user to deploy/keycloak/infra-gate-realm.json (which is
    //     shared with InfraGate.McpGateway.KeycloakTests), and
    //   - implementing antiforgery cookie + form-token scraping needed to POST the
    //     gateway's browser approval endpoints under MapGatewayApprovalEndpoints.
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
    // If a follow-up wants stricter end-to-end coverage for bullet #6, replace this
    // with: (a) a second user in infra-gate-realm.json, (b) AcquireTokenAsync calls
    // for each user, and (c) a helper that GETs /approvals/{id}, captures the
    // antiforgery cookie + hidden __RequestVerificationToken, then POSTs /approve.
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

    public static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

    public async Task<IReadOnlyList<JsonElement>> ReadAuditEventsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(approvalRoot, ApprovalConventions.Storage.AuditFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var events = new List<JsonElement>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
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
                services.AddSingleton<PromptInjectionGuard>();
                services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
                services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
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
}
