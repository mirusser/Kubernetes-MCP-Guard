using System.Net;
using System.Text.RegularExpressions;

namespace InfraGate.Remediation.E2E.Tests;

/// <summary>
/// Drives the real operator approval flow against a live Gateway + Keycloak: submits the
/// access code, follows the redirect into Keycloak's hosted login page, posts real
/// credentials, then approves the resulting challenge. No in-process hosting or OAuth
/// backchannel faking (unlike SafetyE2EFixture) — every hop is a genuine HTTP round trip.
/// </summary>
public sealed partial class OperatorApprovalClient : IDisposable
{
    private const string CodeFormField = "code";
    private const string RequestVerificationTokenField = "__RequestVerificationToken";

    private readonly HttpClient http;
    private readonly Uri gatewayBaseUri;

    public OperatorApprovalClient(Uri gatewayBaseUri)
    {
        this.gatewayBaseUri = gatewayBaseUri;
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        http = new HttpClient(handler);
    }

    /// <summary>
    /// Submits <paramref name="accessCode"/> at <paramref name="approvalCodeUrl"/> (the
    /// "Review:" link from the approval email), logs in to Keycloak as
    /// <paramref name="keycloakUsername"/>, and approves the resulting challenge. Returns the
    /// approved challenge id.
    /// </summary>
    public async Task<string> ApproveAsync(
        string approvalCodeUrl,
        string accessCode,
        string keycloakUsername,
        string keycloakPassword,
        CancellationToken cancellationToken)
    {
        EnsureTrustedGatewayUrl(approvalCodeUrl);

        string codePageToken = await GetAntiforgeryTokenAsync(approvalCodeUrl, cancellationToken)
            .ConfigureAwait(false);

        using var codeContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [CodeFormField] = accessCode,
            [RequestVerificationTokenField] = codePageToken,
        });

        using HttpResponseMessage loginPageResponse = await http
            .PostAsync(approvalCodeUrl, codeContent, cancellationToken)
            .ConfigureAwait(false);
        loginPageResponse.EnsureSuccessStatusCode();
        string loginPageHtml = await loginPageResponse.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        Uri loginActionUri = ParseKeycloakLoginAction(loginPageHtml);

        using var credentialsContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = keycloakUsername,
            ["password"] = keycloakPassword,
        });

        using HttpResponseMessage challengePageResponse = await http
            .PostAsync(loginActionUri, credentialsContent, cancellationToken)
            .ConfigureAwait(false);
        challengePageResponse.EnsureSuccessStatusCode();
        string challengePageHtml = await challengePageResponse.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        string challengeId = ParseChallengeId(challengePageResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty);
        string approveToken = ParseAntiforgeryToken(challengePageHtml);

        var approveUri = new Uri(gatewayBaseUri, $"/approvals/{challengeId}/approve");

        using var approveContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [RequestVerificationTokenField] = approveToken,
        });

        using HttpResponseMessage approveResponse = await http
            .PostAsync(approveUri, approveContent, cancellationToken)
            .ConfigureAwait(false);
        approveResponse.EnsureSuccessStatusCode();

        return challengeId;
    }

    // approvalCodeUrl is sourced from a Mailpit message matched only by subject prefix — an
    // untrusted channel. Refuse to GET/POST to it (and thus never submit the operator's real
    // Keycloak credentials into whatever page it points at) unless its origin exactly matches
    // the configured Gateway.
    private void EnsureTrustedGatewayUrl(string approvalCodeUrl)
    {
        var url = new Uri(approvalCodeUrl, UriKind.Absolute);
        bool sameOrigin = string.Equals(url.Scheme, gatewayBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(url.Host, gatewayBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            && url.Port == gatewayBaseUri.Port;

        if (!sameOrigin)
        {
            throw new InvalidOperationException(
                $"Refusing to submit the access code and operator credentials to untrusted approval URL "
                + $"'{approvalCodeUrl}' — expected origin '{gatewayBaseUri.GetLeftPart(UriPartial.Authority)}'.");
        }
    }

    private async Task<string> GetAntiforgeryTokenAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseAntiforgeryToken(html);
    }

    // Keycloak's default login.ftl theme has no hidden CSRF field: the form's own action URL
    // (with session_code/execution/tab_id/client_id query params) carries all the state needed
    // to correlate the subsequent POST with this session.
    private static Uri ParseKeycloakLoginAction(string html)
    {
        Match match = KeycloakLoginFormActionPattern().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not find the Keycloak login form action in the response.");
        }

        return new Uri(WebUtility.HtmlDecode(match.Groups["action"].Value), UriKind.Absolute);
    }

    [GeneratedRegex(
        "id=\"kc-form-login\"[^>]*action=\"(?<action>[^\"]+)\"",
        RegexOptions.CultureInvariant | RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeycloakLoginFormActionPattern();

    private static string ParseChallengeId(string text) =>
        ChallengeIdPattern().Match(text) is { Success: true } match
            ? match.Groups["id"].Value
            : throw new InvalidOperationException(
                $"Could not extract an approval challenge id from the post-login redirect ('{text}').");

    [GeneratedRegex(
        @"https?://[^/]+/approvals/(?<id>[0-9A-Fa-f]+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChallengeIdPattern();

    private static string ParseAntiforgeryToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" value=\"";

        int valueStart = html.IndexOf(marker, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            throw new InvalidOperationException("Approval page did not contain an antiforgery token.");
        }

        valueStart += marker.Length;
        int valueEnd = html.IndexOf('"', valueStart);
        if (valueEnd < valueStart)
        {
            throw new InvalidOperationException("Approval page contained a malformed antiforgery token.");
        }

        return WebUtility.HtmlDecode(html[valueStart..valueEnd]);
    }

    public void Dispose() => http.Dispose();
}
