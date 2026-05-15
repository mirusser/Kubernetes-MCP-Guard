using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace InfraGate.McpGateway;

internal static class GatewayApprovalEndpoints
{
    public static IEndpointRouteBuilder MapGatewayApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpGatewayConventions.Approvals.LoginPath, Login);
        endpoints.MapGet(
                McpGatewayConventions.Approvals.ChallengeRoute,
                async (
                    string challengeId,
                    GatewayApprovalService approvals,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var page = await approvals.GetApprovalPageAsync(challengeId, cancellationToken);
                    var tokens = antiforgery.GetAndStoreTokens(context);

                    return Results.Content(
                        RenderApprovalPage(page, tokens.RequestToken),
                        "text/html",
                        Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);
        endpoints.MapPost(
                McpGatewayConventions.Approvals.ApproveRoute,
                async (
                    string challengeId,
                    GatewayApprovalService approvals,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var validation = await ValidateAntiforgeryAsync(context, antiforgery);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    var result = await approvals.ApproveChallengeAsync(challengeId, cancellationToken);

                    return Results.Content(RenderDecisionPage(result), "text/html", Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);
        endpoints.MapPost(
                McpGatewayConventions.Approvals.DenyRoute,
                async (
                    string challengeId,
                    GatewayApprovalService approvals,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var validation = await ValidateAntiforgeryAsync(context, antiforgery);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    var result = await approvals.DenyChallengeAsync(challengeId, cancellationToken);

                    return Results.Content(RenderDecisionPage(result), "text/html", Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);

        return endpoints;
    }

    private static IResult Login(HttpContext context)
    {
        var returnUrl = context.Request.Query["ReturnUrl"].ToString();
        if (!IsApprovalReturnUrl(returnUrl))
        {
            returnUrl = McpGatewayConventions.Approvals.PathPrefix;
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            [GatewayAuthConventions.Schemes.ApprovalOAuth]);
    }

    private static async Task<IResult?> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid approval form token.");
        }
    }

    private static bool IsApprovalReturnUrl(string returnUrl) =>
        returnUrl.StartsWith(McpGatewayConventions.Approvals.PathPrefix + "/", StringComparison.Ordinal);

    internal static string RenderApprovalPage(ApprovalPageModel page, string? requestToken)
    {
        var title = page.CanDecide ? "Review Kubernetes Plan" : "Approval Unavailable";
        var body = page.CanDecide && page.Challenge is not null && page.Plan is not null
            ? RenderApprovalForm(page.Challenge, page.Plan, requestToken)
            : $"<p class=\"error\">{Html(page.Error ?? "Approval challenge is unavailable.")}</p>";

        return RenderDocument(title, body);
    }

    internal static string RenderApprovalForm(ApprovalChallenge challenge, KubernetesPlan plan, string? requestToken)
    {
        var dryRun = plan.DryRun!;
        var token = Html(requestToken ?? string.Empty);
        var approveDisabled = plan.PolicyFindings.Any(finding =>
            string.Equals(finding.Severity, "Deny", StringComparison.Ordinal))
            ? " disabled"
            : string.Empty;

        var planSummary = RenderPlanSummaryCard(challenge, plan);
        var objects = RenderObjectsCard(plan);
        var manifest = string.IsNullOrEmpty(plan.Manifest)
            ? string.Empty
            : RenderManifestCard(plan.Manifest);
        var policyFindings = RenderPolicyFindingsCard(plan.PolicyFindings);
        var dryRunSection = RenderDryRunCard(dryRun);
        var diffs = RenderDiffs(plan.Diffs);
        var actions = RenderActionsCard(challenge.Id, token, approveDisabled);

        return planSummary + objects + manifest + policyFindings + dryRunSection + diffs + actions;
    }

    private static string RenderPlanSummaryCard(ApprovalChallenge challenge, KubernetesPlan plan)
    {
        var parameters = plan.Parameters.Count == 0
            ? "<p>None</p>"
            : ("<details><summary>Parameters (" + plan.Parameters.Count + ")</summary><dl>" +
               string.Join(string.Empty,
                   plan.Parameters.Select(kv =>
                       $"<dt>{Html(kv.Key)}</dt><dd><code>{Html(kv.Value)}</code></dd>")) +
               "</dl></details>");

        return $"""
                <section class="card">
                  <h2>Plan Summary</h2>
                  <div class="kv-grid">
                    <span class="kv-label">Plan ID</span>
                    <span class="kv-value"><code>{Html(plan.Id)}</code></span>
                    <span class="kv-label">Operation</span>
                    <span class="kv-value">{Html(plan.Operation)}</span>
                    <span class="kv-label">Namespace</span>
                    <span class="kv-value">{Html(plan.Namespace)}</span>
                    <span class="kv-label">Description</span>
                    <span class="kv-value">{Html(plan.Description)}</span>
                    <span class="kv-label">Plan Created</span>
                    <span class="kv-value">{Html(plan.CreatedAtUtc.ToString("O"))}</span>
                    <span class="kv-label">Challenge Status</span>
                    <span class="kv-value"><span class="badge badge-{Html(challenge.Status)}">{Html(challenge.Status)}</span></span>
                    <span class="kv-label">Plan Hash</span>
                    <span class="kv-value"><code>{Html(challenge.PlanHash)}</code></span>
                    <span class="kv-label">Requester</span>
                    <span class="kv-value">{Html(challenge.RequesterSubject)}</span>
                    <span class="kv-label">Requester Auth</span>
                    <span class="kv-value">{Html(challenge.RequesterAuthenticationType ?? "Unknown")}</span>
                    <span class="kv-label">Challenge Created</span>
                    <span class="kv-value">{Html(challenge.CreatedAtUtc.ToString("O"))}</span>
                    <span class="kv-label">Expires</span>
                    <span class="kv-value">{Html(challenge.ExpiresAtUtc.ToString("O"))}</span>
                  </div>
                  {parameters}
                </section>
                """;
    }

    private static string RenderObjectsCard(KubernetesPlan plan)
    {
        var objects = string.Join(
            string.Empty,
            plan.Objects.Select(obj =>
                $"<li>{Html(obj.ApiVersion)} {Html(obj.Kind)} {Html(obj.Namespace)}/{Html(obj.Name)}</li>"));

        return $"""
                <section class="card">
                  <h2>Objects</h2>
                  <ul>{objects}</ul>
                </section>
                """;
    }

    private static string RenderManifestCard(string manifest)
    {
        return $"""
                <section class="card">
                  <h2>Submitted Manifest</h2>
                  <details>
                    <summary>View manifest</summary>
                    <pre><code>{Html(manifest)}</code></pre>
                  </details>
                </section>
                """;
    }

    private static string RenderPolicyFindingsCard(K8sPlanPolicyFinding[] findings)
    {
        var body = findings.Length == 0
            ? "<p>None</p>"
            : ("<ul>" + string.Join(string.Empty,
                findings.Select(finding =>
                    $"<li><span class=\"badge badge-{Html(finding.Severity.ToLowerInvariant())}\">{Html(finding.Severity)}</span> [{Html(finding.Code)}] {Html(finding.Message)} <span class=\"kv-label\">{Html(finding.ObjectRef)}</span></li>")) + "</ul>");

        return $"""
                <section class="card">
                  <h2>Policy Findings</h2>
                  {body}
                </section>
                """;
    }

    private static string RenderDryRunCard(K8sPlanDryRun dryRun)
    {
        var dryRunObjects = string.Join(
            string.Empty,
            dryRun.Objects.Select(obj => $"<li>{Html(obj.Object)}</li>"));
        var warnings = dryRun.Warnings.Length == 0
            ? "<li>None</li>"
            : string.Join(string.Empty, dryRun.Warnings.Select(warning => $"<li>{Html(warning)}</li>"));

        var statusClass = string.Equals(dryRun.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
            ? "success" : "error";

        return $"""
                <section class="card">
                  <h2>Dry-run Results</h2>
                  <p class="{statusClass}">Server-side dry-run: {Html(dryRun.Status)}</p>
                  <div class="kv-grid">
                    <span class="kv-label">Checked at UTC</span>
                    <span class="kv-value">{Html(dryRun.CheckedAtUtc.ToString("O"))}</span>
                    <span class="kv-label">Message</span>
                    <span class="kv-value">{Html(dryRun.Message)}</span>
                  </div>
                  <h3>Dry-run Objects</h3>
                  <ul>{dryRunObjects}</ul>
                  <h3>Admission Warnings</h3>
                  <ul>{warnings}</ul>
                </section>
                """;
    }

    private static string RenderActionsCard(string challengeId, string token, string approveDisabled)
    {
        return $"""
                <section class="card actions-card">
                  <form method="post" action="{McpGatewayConventions.Approvals.PathPrefix}/{Html(challengeId)}/approve">
                    <input type="hidden" name="{McpGatewayConventions.Approvals.RequestVerificationToken}" value="{token}">
                    <button type="submit" class="approve"{approveDisabled}>Approve</button>
                  </form>
                  <form method="post" action="{McpGatewayConventions.Approvals.PathPrefix}/{Html(challengeId)}/deny">
                    <input type="hidden" name="{McpGatewayConventions.Approvals.RequestVerificationToken}" value="{token}">
                    <button type="submit" class="deny">Deny</button>
                  </form>
                </section>
                """;
    }

    internal static string RenderPolicyFindings(K8sPlanPolicyFinding[] findings)
    {
        if (findings.Length == 0)
        {
            return "<p>None</p>";
        }

        var items = string.Join(
            string.Empty,
            findings.Select(finding =>
                $"<li><span class=\"badge badge-{Html(finding.Severity.ToLowerInvariant())}\">{Html(finding.Severity)}</span> [{Html(finding.Code)}] {Html(finding.Message)} <span class=\"kv-label\">{Html(finding.ObjectRef)}</span></li>"));

        return $"<ul>{items}</ul>";
    }

    internal static string RenderDiffs(K8sPlanDiff[] diffs)
    {
        if (diffs.Length == 0)
        {
            return """
                   <section class="card">
                     <h2>Diff</h2>
                     <p class="error">No diff was recorded for this plan.</p>
                   </section>
                   """;
        }

        return """
               <section class="card">
                 <h2>Diff</h2>
               """ + string.Join(string.Empty, diffs.Select(RenderDiff)) + "</section>";
    }

    internal static string RenderDiff(K8sPlanDiff diff)
    {
        var paths = RenderDiffPaths(diff);

        return $"""
               <div class="diff-block">
                 <h3>{Html(diff.Summary)}</h3>
                 <div class="kv-grid">
                   <span class="kv-label">Change</span>
                   <span class="kv-value">{Html(diff.ChangeType)}</span>
                   <span class="kv-label">Object</span>
                   <span class="kv-value">{Html(diff.Object.ApiVersion)} {Html(diff.Object.Kind)} {Html(diff.Object.Namespace)}/{Html(diff.Object.Name)}</span>
                 </div>
                 {paths}
                 <pre><code>{Html(diff.UnifiedDiff)}</code></pre>
               </div>
               """;
    }

    internal static string RenderDiffPaths(K8sPlanDiff diff)
    {
        return $"""
               <details>
                 <summary>Changed paths</summary>
                 <div class="kv-grid">
                   <span class="kv-label">Added</span>
                   <span class="kv-value">{RenderPathList(diff.AddedPaths)}</span>
                   <span class="kv-label">Removed</span>
                   <span class="kv-value">{RenderPathList(diff.RemovedPaths)}</span>
                   <span class="kv-label">Changed</span>
                   <span class="kv-value">{RenderPathList(diff.ChangedPaths)}</span>
                 </div>
               </details>
               """;
    }

    internal static string RenderPathList(string[] paths)
    {
        if (paths.Length == 0)
        {
            return "None";
        }

        return string.Join(", ", paths.Select(path => $"<code>{Html(path)}</code>"));
    }

    internal static string RenderDecisionPage(ApprovalDecisionResult result)
    {
        var className = result.Succeeded ? "success" : "error";

        return RenderDocument(
            result.Succeeded ? "Approval Recorded" : "Approval Failed",
            $"""
             <section class="card">
               <p class="{className}">{Html(result.Message)}</p>
             </section>
             """);
    }

    internal static string RenderDocument(string title, string body)
    {
        return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>{{Html(title)}} - InfraGate</title>
                  <style>
                    :root {
                      color-scheme: dark;
                      font-family: system-ui, -apple-system, sans-serif;
                      --color-bg: #0f1117;
                      --color-card-bg: #1a1d28;
                      --color-text: #e1e4ea;
                      --color-text-muted: #8b8fa3;
                      --color-border: #2a2d3a;
                      --color-code-bg: #0d0f16;
                      --color-pre-bg: #0b0d14;
                      --color-pre-border: #2a2d3a;
                      --color-success: #34d399;
                      --color-error: #f87171;
                      --color-warning: #fbbf24;
                      --color-info: #60a5fa;
                      --color-approve: #059669;
                      --color-deny: #dc2626;
                      --color-badge-deny-bg: #7f1d1d;
                      --color-badge-deny-text: #fca5a5;
                      --color-badge-warn-bg: #78350f;
                      --color-badge-warn-text: #fcd34d;
                      --color-badge-info-bg: #1e3a5f;
                      --color-badge-info-text: #93c5fd;
                      --color-badge-pending-bg: #1e3a5f;
                      --color-badge-pending-text: #93c5fd;
                    }
                    @media (prefers-color-scheme: light) {
                      :root {
                        color-scheme: light;
                        --color-bg: #f8f9fb;
                        --color-card-bg: #ffffff;
                        --color-text: #1f2937;
                        --color-text-muted: #6b7280;
                        --color-border: #e2e4e9;
                        --color-code-bg: #f3f4f6;
                        --color-pre-bg: #f9fafb;
                        --color-pre-border: #d1d5db;
                        --color-success: #166534;
                        --color-error: #991b1b;
                        --color-badge-deny-bg: #fee2e2;
                        --color-badge-deny-text: #991b1b;
                        --color-badge-warn-bg: #fef3c7;
                        --color-badge-warn-text: #92400e;
                        --color-badge-info-bg: #dbeafe;
                        --color-badge-info-text: #1e40af;
                        --color-badge-pending-bg: #dbeafe;
                        --color-badge-pending-text: #1e40af;
                      }
                    }
                    body {
                      margin: 0;
                      background: var(--color-bg);
                      color: var(--color-text);
                      line-height: 1.6;
                    }
                    main {
                      max-width: 920px;
                      margin: 0 auto;
                      padding: 40px 24px;
                    }
                    h1 {
                      font-size: 26px;
                      font-weight: 700;
                      margin: 0 0 32px;
                      color: var(--color-text);
                    }
                    h2 {
                      font-size: 17px;
                      font-weight: 650;
                      margin: 0 0 14px;
                      color: var(--color-text);
                    }
                    h3 {
                      font-size: 15px;
                      font-weight: 600;
                      margin: 20px 0 10px;
                      color: var(--color-text);
                    }
                    h3:first-child {
                      margin-top: 0;
                    }
                    .card {
                      background: var(--color-card-bg);
                      border: 1px solid var(--color-border);
                      border-radius: 10px;
                      padding: 24px;
                      margin-bottom: 16px;
                    }
                    .kv-grid {
                      display: grid;
                      grid-template-columns: minmax(140px, 190px) 1fr;
                      gap: 10px 20px;
                      margin: 8px 0;
                    }
                    .kv-label {
                      font-weight: 650;
                      color: var(--color-text-muted);
                      font-size: 14px;
                    }
                    .kv-value {
                      color: var(--color-text);
                      font-size: 14px;
                      overflow-wrap: anywhere;
                    }
                    code {
                      font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
                      font-size: 13px;
                      background: var(--color-code-bg);
                      padding: 2px 6px;
                      border-radius: 4px;
                      color: var(--color-info);
                    }
                    .kv-value code {
                      background: none;
                      padding: 0;
                      border-radius: 0;
                    }
                    ul {
                      padding-left: 22px;
                      margin: 6px 0;
                    }
                    ul li {
                      margin-bottom: 4px;
                      font-size: 14px;
                    }
                    pre {
                      overflow-x: auto;
                      padding: 16px;
                      border: 1px solid var(--color-pre-border);
                      border-radius: 8px;
                      background: var(--color-pre-bg);
                      margin: 12px 0 0;
                      font-size: 13px;
                      line-height: 1.5;
                    }
                    pre code {
                      background: none;
                      padding: 0;
                      border-radius: 0;
                      color: var(--color-text);
                    }
                    details {
                      margin: 10px 0 12px;
                      cursor: pointer;
                    }
                    details summary {
                      font-weight: 600;
                      font-size: 14px;
                      color: var(--color-text-muted);
                      padding: 4px 0;
                    }
                    details .kv-grid {
                      margin-top: 10px;
                    }
                    .diff-block {
                      margin-top: 20px;
                      padding-top: 16px;
                      border-top: 1px solid var(--color-border);
                    }
                    .diff-block:first-child {
                      margin-top: 0;
                      padding-top: 0;
                      border-top: none;
                    }
                    .badge {
                      display: inline-block;
                      padding: 2px 8px;
                      border-radius: 4px;
                      font-size: 12px;
                      font-weight: 650;
                      text-transform: uppercase;
                      letter-spacing: 0.3px;
                    }
                    .badge-deny { background: var(--color-badge-deny-bg); color: var(--color-badge-deny-text); }
                    .badge-warn { background: var(--color-badge-warn-bg); color: var(--color-badge-warn-text); }
                    .badge-info { background: var(--color-badge-info-bg); color: var(--color-badge-info-text); }
                    .badge-pending { background: var(--color-badge-pending-bg); color: var(--color-badge-pending-text); }
                    .error { color: var(--color-error); font-weight: 700; }
                    .success { color: var(--color-success); font-weight: 700; }
                    .actions-card {
                      display: flex;
                      gap: 14px;
                      flex-wrap: wrap;
                      align-items: center;
                    }
                    button {
                      min-width: 120px;
                      min-height: 42px;
                      border: 1px solid var(--color-border);
                      border-radius: 8px;
                      background: var(--color-card-bg);
                      color: var(--color-text);
                      font-weight: 650;
                      font-size: 14px;
                      cursor: pointer;
                      padding: 0 20px;
                      transition: opacity 0.15s;
                    }
                    button:hover {
                      opacity: 0.85;
                    }
                    button.approve {
                      border-color: var(--color-approve);
                      background: var(--color-approve);
                      color: #ffffff;
                    }
                    button.deny {
                      border-color: var(--color-deny);
                      background: var(--color-deny);
                      color: #ffffff;
                    }
                    button:disabled {
                      opacity: 0.45;
                      cursor: not-allowed;
                    }
                    button:disabled:hover {
                      opacity: 0.45;
                    }
                  </style>
                </head>
                <body>
                  <main>
                    <h1>{{Html(title)}}</h1>
                    {{body}}
                  </main>
                </body>
                </html>
                """;
    }

    internal static string Html(string value) =>
        WebUtility.HtmlEncode(value);
}
