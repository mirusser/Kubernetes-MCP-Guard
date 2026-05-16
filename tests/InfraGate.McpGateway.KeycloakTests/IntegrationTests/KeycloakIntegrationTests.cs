using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
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
    private const string MasterRealmName = "master";
    private const string RealmJsonFileName = "infra-gate-realm.json";
    private const string AdminClientId = "admin-cli";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";
    private const string McpClientId = "mcp-client";
    private const string SmokeClientId = "mcp-smoke-client";
    private const string LimitedClientId = "mcp-client-limited";
    private const string DemoUsername = "demo";
    private const string DemoPassword = "demo";
    private const string AuthCodeRedirectUri = "http://127.0.0.1:9876/callback";
    private const string AuthCodeState = "mcp-client-pkce-state";
    private const string AuthCodeVerifier = "mcp-client-auth-code-pkce-verifier-with-enough-entropy-1234567890";
    private const string WrongAuthCodeVerifier = "wrong-auth-code-pkce-verifier-with-enough-entropy-1234567890";
    private const string Resource = GatewayAuthConventions.DefaultOAuthResource;
    private const string Scope = GatewayAuthConventions.DefaultOAuthScope;
    private const string OpenIdScope = "openid profile email " + Scope;
    private const string S256CodeChallengeMethod = "S256";
    private const string AuthorizationCodeGrantType = "authorization_code";
    private const string PasswordGrantType = "password";
    private const string CodeResponseType = "code";
    private const string InvalidGrantOAuthError = "invalid_grant";
    private const string LoginFormId = "kc-form-login";
    private const string LoginActionAttribute = "action=\"";
    private const int MaxRedirects = 8;

    private KeycloakContainer? keycloakContainer;
    private string keycloakBaseAddress = string.Empty;

    public async Task InitializeAsync()
    {
        string realmJsonPath = Path.Combine(AppContext.BaseDirectory, "TestData", RealmJsonFileName);

        keycloakContainer = new KeycloakBuilder(KeycloakImage)
            .WithUsername(AdminUsername)
            .WithPassword(AdminPassword)
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
        var plan = CreateApprovalPlan();
        var planResult = await approvalStore.CreatePlanAsync(plan, "mcp-nginx-demo", CancellationToken.None);
        var challenge = await challengeStore.CreateAsync(
            planResult.Envelope.Id,
            planResult.Hash,
            DemoUsername,
            GatewayAuthConventions.Audit.OAuthAuthenticationType,
            McpGatewayOptions.DefaultApprovalChallengeTtl,
            planResult.Envelope.IntentDigest,
            planResult.Envelope.ReviewDigest,
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

    [Fact]
    public async Task McpClientAuthorizationCodePkceFlow_ValidVerifier_ReturnsGatewayAcceptedToken()
    {
        using var browser = await CreateAuthenticatedKeycloakBrowserAsync();
        string code = await RequestAuthorizationCodeAsync(browser, AuthCodeVerifier);
        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code, AuthCodeVerifier);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        JsonElement tokenRoot = tokenDocument.RootElement;
        string accessToken = tokenRoot.GetProperty(KeycloakJson.AccessToken).GetString()
                             ?? throw new InvalidOperationException("Token response did not contain access_token.");
        using var jwtDocument = DecodeJwtPayload(accessToken);
        JsonElement jwtRoot = jwtDocument.RootElement;
        Assert.Contains(Resource, GetAudiences(jwtRoot));
        Assert.Contains(Scope, jwtRoot.GetProperty(KeycloakJson.Scope).GetString()!.Split(' '));
        Assert.Equal(DemoUsername, jwtRoot.GetProperty(GatewayAuthConventions.Claims.Subject).GetString());

        using var server = CreateGatewayServer(authority: RealmAuthority());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayAuthConventions.AuthorizationScheme,
            accessToken);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task McpClientAuthorizationCodePkceFlow_WrongVerifier_RejectsTokenExchange()
    {
        using var browser = await CreateAuthenticatedKeycloakBrowserAsync();
        string code = await RequestAuthorizationCodeAsync(browser, AuthCodeVerifier);

        using var response = await ExchangeAuthorizationCodeAsync(code, WrongAuthCodeVerifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errorDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(InvalidGrantOAuthError, errorDocument.RootElement.GetProperty(KeycloakJson.Error).GetString());
    }

    private string RealmAuthority() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}";

    private string MasterTokenEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{MasterRealmName}/protocol/openid-connect/token";

    private string TokenEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}/protocol/openid-connect/token";

    private string AuthorizationEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/realms/{RealmName}/protocol/openid-connect/auth";

    private string RegistrationEndpoint() =>
        $"{RealmAuthority()}/clients-registrations/openid-connect";

    private string AdminUsersEndpoint() =>
        $"{keycloakBaseAddress.TrimEnd('/')}/admin/realms/{RealmName}/users";

    private string AdminUserImpersonationEndpoint(string userId) =>
        $"{AdminUsersEndpoint()}/{Uri.EscapeDataString(userId)}/impersonation";

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
            new(KeycloakParameters.GrantType, PasswordGrantType),
            new(KeycloakParameters.ClientId, clientId),
            new(KeycloakParameters.Username, DemoUsername),
            new(KeycloakParameters.Password, DemoPassword)
        };

        if (!string.IsNullOrWhiteSpace(requestedScope))
        {
            formValues.Add(new(KeycloakParameters.Scope, requestedScope));
        }

        using var content = new FormUrlEncodedContent(formValues);

        var response = await http.PostAsync(TokenEndpoint(), content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty(KeycloakJson.AccessToken).GetString()
               ?? throw new InvalidOperationException("Token response did not contain access_token.");
    }

    private async Task<HttpClient> CreateAuthenticatedKeycloakBrowserAsync(CancellationToken cancellationToken = default)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
        var browser = new HttpClient(handler)
        {
            BaseAddress = new Uri(keycloakBaseAddress)
        };

        string adminToken = await AcquireAdminTokenAsync(cancellationToken);
        string userId = await FindUserIdAsync(adminToken, DemoUsername, cancellationToken);
        using var impersonationRequest = new HttpRequestMessage(
            HttpMethod.Post,
            AdminUserImpersonationEndpoint(userId));
        impersonationRequest.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayAuthConventions.AuthorizationScheme,
            adminToken);
        using var impersonation = await browser.SendAsync(impersonationRequest, cancellationToken);
        AddResponseCookies(browser, impersonation);
        impersonation.EnsureSuccessStatusCode();
        await FollowImpersonationRedirectAsync(browser, impersonation, cancellationToken);

        return browser;
    }

    private async Task<string> AcquireAdminTokenAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var content = new FormUrlEncodedContent(
        [
            new(KeycloakParameters.GrantType, PasswordGrantType),
            new(KeycloakParameters.ClientId, AdminClientId),
            new(KeycloakParameters.Username, AdminUsername),
            new(KeycloakParameters.Password, AdminPassword)
        ]);
        var response = await http.PostAsync(MasterTokenEndpoint(), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return document.RootElement.GetProperty(KeycloakJson.AccessToken).GetString()
               ?? throw new InvalidOperationException("Admin token response did not contain access_token.");
    }

    private async Task<string> FindUserIdAsync(string adminToken, string username, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayAuthConventions.AuthorizationScheme,
            adminToken);
        string usersUri = QueryHelpers.AddQueryString(
            AdminUsersEndpoint(),
            new Dictionary<string, string?>
            {
                [KeycloakParameters.Username] = username,
                [KeycloakParameters.Exact] = bool.TrueString.ToLowerInvariant()
            });
        var response = await http.GetAsync(usersUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var users = document.RootElement.EnumerateArray().ToArray();
        if (users.Length == 0)
        {
            throw new InvalidOperationException($"Keycloak user '{username}' was not found.");
        }

        return users[0].GetProperty(KeycloakJson.Id).GetString()
               ?? throw new InvalidOperationException($"Keycloak user '{username}' did not contain id.");
    }

    private async Task FollowImpersonationRedirectAsync(
        HttpClient browser,
        HttpResponseMessage impersonation,
        CancellationToken cancellationToken)
    {
        var redirect = await TryReadImpersonationRedirectAsync(impersonation, cancellationToken);
        if (redirect is null)
        {
            return;
        }

        var redirectUri = redirect.IsAbsoluteUri
            ? redirect
            : new Uri(browser.BaseAddress!, redirect);
        for (int i = 0; i < MaxRedirects; i++)
        {
            using var response = await browser.GetAsync(redirectUri, cancellationToken);
            AddResponseCookies(browser, response);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                return;
            }

            redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(redirectUri, response.Headers.Location);
        }

        throw new InvalidOperationException("Keycloak impersonation redirect chain did not terminate.");
    }

    private static async Task<Uri?> TryReadImpersonationRedirectAsync(
        HttpResponseMessage impersonation,
        CancellationToken cancellationToken)
    {
        if (impersonation.Headers.Location is not null)
        {
            return impersonation.Headers.Location;
        }

        string json = await impersonation.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind is JsonValueKind.Object &&
            document.RootElement.TryGetProperty(KeycloakJson.Redirect, out var redirect) &&
            !string.IsNullOrWhiteSpace(redirect.GetString()))
        {
            return new Uri(redirect.GetString()!, UriKind.RelativeOrAbsolute);
        }

        return null;
    }

    private async Task<string> RequestAuthorizationCodeAsync(
        HttpClient browser,
        string codeVerifier,
        bool includeResource = true,
        CancellationToken cancellationToken = default)
    {
        string authUri = BuildAuthorizationUri(codeVerifier, includeResource);
        using var response = await browser.GetAsync(authUri, cancellationToken);
        AddResponseCookies(browser, response);
        if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
        {
            return ReadAuthorizationCode(response.Headers.Location);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateAuthorizationRedirectException(response, body);
        }

        string loginPage = await response.Content.ReadAsStringAsync(cancellationToken);
        using HttpResponseMessage loginResponse = await SubmitLoginFormAsync(
            browser,
            authUri,
            loginPage,
            cancellationToken);

        return await FollowAuthorizationRedirectAsync(browser, loginResponse, cancellationToken);
    }

    private static string ReadAuthorizationCode(Uri redirect)
    {
        Assert.Equal(AuthCodeRedirectUri, redirect.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal(AuthCodeState, query[KeycloakParameters.State].ToString());

        return query[KeycloakParameters.Code].ToString();
    }

    private async Task<HttpResponseMessage> SubmitLoginFormAsync(
        HttpClient browser,
        string authUri,
        string loginPage,
        CancellationToken cancellationToken)
    {
        string loginAction = ParseLoginFormAction(authUri, loginPage);
        using var content = new FormUrlEncodedContent(
        [
            new(KeycloakParameters.Username, DemoUsername),
            new(KeycloakParameters.Password, DemoPassword),
            new(KeycloakParameters.CredentialId, string.Empty),
            new(KeycloakParameters.Login, "Sign In")
        ]);
        HttpResponseMessage response = await browser.PostAsync(loginAction, content, cancellationToken);
        AddResponseCookies(browser, response);
        await response.Content.LoadIntoBufferAsync(cancellationToken);

        return response;
    }

    private static string ParseLoginFormAction(string authUri, string html)
    {
        int formIdIndex = html.IndexOf(LoginFormId, StringComparison.Ordinal);
        if (formIdIndex < 0)
        {
            throw new InvalidOperationException("Keycloak login page did not contain the expected login form.");
        }

        int formStart = html.LastIndexOf("<form", formIdIndex, StringComparison.OrdinalIgnoreCase);
        int formEnd = html.IndexOf('>', formIdIndex);
        if (formStart < 0 || formEnd < formStart)
        {
            throw new InvalidOperationException("Keycloak login form was malformed.");
        }

        int actionStart = html.IndexOf(
            LoginActionAttribute,
            formStart,
            formEnd - formStart,
            StringComparison.OrdinalIgnoreCase);
        if (actionStart < 0)
        {
            throw new InvalidOperationException("Keycloak login form did not contain an action attribute.");
        }

        actionStart += LoginActionAttribute.Length;
        int actionEnd = html.IndexOf('"', actionStart);
        if (actionEnd < actionStart)
        {
            throw new InvalidOperationException("Keycloak login form action was malformed.");
        }

        string action = WebUtility.HtmlDecode(html[actionStart..actionEnd]);
        Uri baseUri = new(authUri);
        return new Uri(baseUri, action).ToString();
    }

    private static async Task<string> FollowAuthorizationRedirectAsync(
        HttpClient browser,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Uri? currentUri = response.RequestMessage?.RequestUri;
        for (int i = 0; i < MaxRedirects; i++)
        {
            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                Uri redirect = ResolveRedirect(currentUri, response.Headers.Location);
                if (redirect.GetLeftPart(UriPartial.Path) == AuthCodeRedirectUri)
                {
                    return ReadAuthorizationCode(redirect);
                }

                response.Dispose();
                response = await browser.GetAsync(redirect, cancellationToken);
                AddResponseCookies(browser, response);
                currentUri = redirect;
                continue;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateAuthorizationRedirectException(response, body);
        }

        throw new InvalidOperationException("Keycloak authorization redirect chain did not terminate.");
    }

    private static Uri ResolveRedirect(Uri? currentUri, Uri redirect) =>
        redirect.IsAbsoluteUri
            ? redirect
            : new Uri(currentUri ?? new Uri(AuthCodeRedirectUri), redirect);

    private static InvalidOperationException CreateAuthorizationRedirectException(
        HttpResponseMessage response,
        string body) =>
        new($"Expected Keycloak authorization redirect, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");

    private string BuildAuthorizationUri(string codeVerifier, bool includeResource)
    {
        var query = new Dictionary<string, string?>
        {
            [KeycloakParameters.ResponseType] = CodeResponseType,
            [KeycloakParameters.ClientId] = McpClientId,
            [KeycloakParameters.RedirectUri] = AuthCodeRedirectUri,
            [KeycloakParameters.Scope] = OpenIdScope,
            [KeycloakParameters.State] = AuthCodeState,
            [KeycloakParameters.CodeChallenge] = CodeChallenge(codeVerifier),
            [KeycloakParameters.CodeChallengeMethod] = S256CodeChallengeMethod
        };
        if (includeResource)
        {
            query[GatewayAuthConventions.Parameters.Resource] = Resource;
        }

        return QueryHelpers.AddQueryString(AuthorizationEndpoint(), query);
    }

    private async Task<HttpResponseMessage> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        bool includeResource = true,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        var formValues = new List<KeyValuePair<string, string>>
        {
            new(KeycloakParameters.GrantType, AuthorizationCodeGrantType),
            new(KeycloakParameters.ClientId, McpClientId),
            new(KeycloakParameters.Code, code),
            new(KeycloakParameters.RedirectUri, AuthCodeRedirectUri),
            new(KeycloakParameters.CodeVerifier, codeVerifier)
        };
        if (includeResource)
        {
            formValues.Add(new(GatewayAuthConventions.Parameters.Resource, Resource));
        }

        using var content = new FormUrlEncodedContent(formValues);
        var response = await http.PostAsync(TokenEndpoint(), content, cancellationToken);
        await response.Content.LoadIntoBufferAsync(cancellationToken);

        return response;
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

    private static string CodeChallenge(string codeVerifier) =>
        EncodeBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static void AssertClientRegistrationRejected(HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"Expected DCR policy rejection, got {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static PlanEnvelope<KubernetesPlanPayload> CreateApprovalPlan()
    {
        var objects = new[] { new K8sObjectRef("apps/v1", "Deployment", "mcp-nginx-demo", "nginx-demo") };

        var payload = new KubernetesPlanPayload(
            "mcp-nginx-demo",
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

        return KubernetesApprovalAdapter.CreateEnvelope(
            ApprovalStore.NewPlanId(),
            "scale",
            DateTimeOffset.UtcNow,
            new PlanRequester(DemoUsername, GatewayAuthConventions.Audit.OAuthAuthenticationType),
            payload);
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
                services.AddSingleton<IPlanReviewAdapter, KubernetesPlanReviewAdapter>();
                services.AddSingleton<IPlanReviewRenderer, KubernetesPlanReviewRenderer>();
                services.AddSingleton<GatewayApprovalService>();
                services.AddHttpContextAccessor();
                services.AddLogging();
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
                new(KeycloakParameters.GrantType, PasswordGrantType),
                new(KeycloakParameters.ClientId, SmokeClientId),
                new(KeycloakParameters.Username, DemoUsername),
                new(KeycloakParameters.Password, DemoPassword),
                new(KeycloakParameters.Scope, Scope)
            ]);
            var tokenResponse = await http.PostAsync(tokenEndpoint, content, cancellationToken);
            tokenResponse.EnsureSuccessStatusCode();
            using var tokenDocument = await JsonDocument.ParseAsync(
                await tokenResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            string accessToken = tokenDocument.RootElement.GetProperty(KeycloakJson.AccessToken).GetString()
                                 ?? throw new InvalidOperationException("Keycloak token response did not contain access_token.");
            var json = JsonSerializer.Serialize(new
            {
                access_token = accessToken,
                token_type = GatewayAuthConventions.AuthorizationScheme,
                expires_in = 300,
                scope = Scope
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static class KeycloakParameters
    {
        public const string ClientId = "client_id";
        public const string Code = "code";
        public const string CodeChallenge = "code_challenge";
        public const string CodeChallengeMethod = "code_challenge_method";
        public const string CodeVerifier = "code_verifier";
        public const string CredentialId = "credentialId";
        public const string Exact = "exact";
        public const string GrantType = "grant_type";
        public const string Login = "login";
        public const string Password = "password";
        public const string RedirectUri = "redirect_uri";
        public const string ResponseType = "response_type";
        public const string Scope = "scope";
        public const string State = "state";
        public const string Username = "username";
    }

    private static class KeycloakJson
    {
        public const string AccessToken = "access_token";
        public const string Error = "error";
        public const string Id = "id";
        public const string Redirect = "redirect";
        public const string Scope = "scope";
    }
}
