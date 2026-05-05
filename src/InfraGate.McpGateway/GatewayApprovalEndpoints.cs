using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using InfraGate.Approvals;
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

    private static string RenderApprovalPage(ApprovalPageModel page, string? requestToken)
    {
        var title = page.CanDecide ? "Review Kubernetes Plan" : "Approval Unavailable";
        var body = page.CanDecide && page.Challenge is not null && page.Plan is not null
            ? RenderApprovalForm(page.Challenge, page.Plan, requestToken)
            : $"<p class=\"error\">{Html(page.Error ?? "Approval challenge is unavailable.")}</p>";

        return RenderDocument(title, body);
    }

    private static string RenderApprovalForm(ApprovalChallenge challenge, K8sPlan plan, string? requestToken)
    {
        var objects = string.Join(
            string.Empty,
            plan.Objects.Select(obj =>
                $"<li>{Html(obj.ApiVersion)} {Html(obj.Kind)} {Html(obj.Namespace)}/{Html(obj.Name)}</li>"));
        var token = Html(requestToken ?? string.Empty);

        return $"""
               <dl>
                 <dt>PlanId</dt><dd>{Html(plan.Id)}</dd>
                 <dt>Operation</dt><dd>{Html(plan.Operation)}</dd>
                 <dt>Namespace</dt><dd>{Html(plan.Namespace)}</dd>
                 <dt>Description</dt><dd>{Html(plan.Description)}</dd>
                 <dt>Plan hash</dt><dd><code>{Html(challenge.PlanHash)}</code></dd>
                 <dt>Requester</dt><dd>{Html(challenge.RequesterSubject)}</dd>
                 <dt>Expires at UTC</dt><dd>{Html(challenge.ExpiresAtUtc.ToString("O"))}</dd>
               </dl>
               <h2>Objects</h2>
               <ul>{objects}</ul>
               <div class="actions">
                 <form method="post" action="{McpGatewayConventions.Approvals.PathPrefix}/{Html(challenge.Id)}/approve">
                   <input type="hidden" name="{McpGatewayConventions.Approvals.RequestVerificationToken}" value="{token}">
                   <button type="submit" class="approve">Approve</button>
                 </form>
                 <form method="post" action="{McpGatewayConventions.Approvals.PathPrefix}/{Html(challenge.Id)}/deny">
                   <input type="hidden" name="{McpGatewayConventions.Approvals.RequestVerificationToken}" value="{token}">
                   <button type="submit">Deny</button>
                 </form>
               </div>
               """;
    }

    private static string RenderDecisionPage(ApprovalDecisionResult result)
    {
        var className = result.Succeeded ? "success" : "error";

        return RenderDocument(
            result.Succeeded ? "Approval Recorded" : "Approval Failed",
            $"<p class=\"{className}\">{Html(result.Message)}</p>");
    }

    private static string RenderDocument(string title, string body)
    {
        return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>{{Html(title)}} - InfraGate</title>
                  <style>
                    :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
                    body { margin: 0; background: #f7f7f4; color: #1f2933; }
                    main { max-width: 880px; margin: 0 auto; padding: 32px 20px; }
                    h1 { font-size: 28px; margin: 0 0 24px; }
                    h2 { font-size: 18px; margin-top: 28px; }
                    dl { display: grid; grid-template-columns: minmax(130px, 180px) 1fr; gap: 10px 18px; }
                    dt { font-weight: 700; color: #334155; }
                    dd { margin: 0; overflow-wrap: anywhere; }
                    code { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 13px; }
                    ul { padding-left: 22px; }
                    .actions { display: flex; gap: 12px; flex-wrap: wrap; margin-top: 28px; }
                    button { min-width: 104px; min-height: 40px; border: 1px solid #a3a3a3; border-radius: 6px; background: #ffffff; color: #111827; font-weight: 700; cursor: pointer; }
                    button.approve { border-color: #0f766e; background: #0f766e; color: #ffffff; }
                    .error { color: #991b1b; font-weight: 700; }
                    .success { color: #166534; font-weight: 700; }
                    @media (prefers-color-scheme: dark) {
                      body { background: #111827; color: #e5e7eb; }
                      dt { color: #cbd5e1; }
                      button { background: #1f2937; color: #f9fafb; }
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

    private static string Html(string value) =>
        WebUtility.HtmlEncode(value);
}
