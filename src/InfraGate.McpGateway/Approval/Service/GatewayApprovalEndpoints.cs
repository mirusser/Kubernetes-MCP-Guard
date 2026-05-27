using System.Text;
using InfraGate.Approvals;
using InfraGate.ApprovalUi;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace InfraGate.McpGateway;

internal static class GatewayApprovalEndpoints
{
    private const string TextHtmlContentType = "text/html";
    public static IEndpointRouteBuilder MapGatewayApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpGatewayConventions.Approvals.LoginPath, Login);
        endpoints.MapGet(McpGatewayConventions.Approvals.LogoutPath, Logout);
        endpoints.MapGet(
            McpGatewayConventions.Approvals.CodeRoute,
            async (
                [FromServices] IApprovalPageRenderer renderer,
                HttpContext context,
                IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                var html = await renderer.RenderCodePageAsync(BuildCodePageData(tokens.RequestToken, null, null))
                    .ConfigureAwait(false);

                return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
            });
        endpoints.MapPost(
            McpGatewayConventions.Approvals.CodeRoute,
            async (
                IApprovalAccessCodeStore accessCodes,
                [FromServices] IApprovalPageRenderer renderer,
                HttpContext context,
                IAntiforgery antiforgery,
                CancellationToken cancellationToken) =>
            {
                var validation = await ValidateAntiforgeryAsync(context, antiforgery).ConfigureAwait(false);
                if (validation is not null)
                {
                    return validation;
                }

                var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                string submittedCode = form[McpGatewayConventions.Approvals.CodeFormField].ToString();
                var result = await accessCodes.ConsumeAsync(submittedCode, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded && result.ChallengeId is not null)
                {
                    return Results.Redirect($"{McpGatewayConventions.Approvals.PathPrefix}/{result.ChallengeId}");
                }

                var tokens = antiforgery.GetAndStoreTokens(context);
                var html = await renderer.RenderCodePageAsync(
                        BuildCodePageData(tokens.RequestToken, submittedCode, result.Message))
                    .ConfigureAwait(false);

                return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
            });
        endpoints.MapGet(
                McpGatewayConventions.Approvals.ChallengeRoute,
                async (
                    string challengeId,
                    IGatewayApprovalService approvals,
                    [FromServices] IApprovalPageRenderer renderer,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var page = await approvals.GetApprovalPageAsync(challengeId, cancellationToken).ConfigureAwait(false);
                    var tokens = antiforgery.GetAndStoreTokens(context);

                    var approvalData = BuildApprovalPageData(page, tokens.RequestToken);
                    var html = await renderer.RenderApprovalPageAsync(approvalData).ConfigureAwait(false);

                    return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);
        endpoints.MapPost(
                McpGatewayConventions.Approvals.ApproveRoute,
                async (
                    string challengeId,
                    IGatewayApprovalService approvals,
                    [FromServices] IApprovalPageRenderer renderer,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var validation = await ValidateAntiforgeryAsync(context, antiforgery).ConfigureAwait(false);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    var result = await approvals.ApproveChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
                    var decisionData = BuildDecisionPageData(result);
                    var html = await renderer.RenderDecisionPageAsync(decisionData).ConfigureAwait(false);

                    return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);
        endpoints.MapPost(
                McpGatewayConventions.Approvals.DenyRoute,
                async (
                    string challengeId,
                    IGatewayApprovalService approvals,
                    [FromServices] IApprovalPageRenderer renderer,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var validation = await ValidateAntiforgeryAsync(context, antiforgery).ConfigureAwait(false);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    var result = await approvals.DenyChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
                    var decisionData = BuildDecisionPageData(result);
                    var html = await renderer.RenderDecisionPageAsync(decisionData).ConfigureAwait(false);

                    return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
                })
            .RequireAuthorization(GatewayAuthConventions.Schemes.ApprovalPolicyName);
        endpoints.MapPost(
                McpGatewayConventions.Approvals.CancelRoute,
                async (
                    string challengeId,
                    IGatewayApprovalService approvals,
                    [FromServices] IApprovalPageRenderer renderer,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    CancellationToken cancellationToken) =>
                {
                    var validation = await ValidateAntiforgeryAsync(context, antiforgery).ConfigureAwait(false);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    var result = await approvals.CancelChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
                    var decisionData = BuildDecisionPageData(result);
                    var html = await renderer.RenderDecisionPageAsync(decisionData).ConfigureAwait(false);

                    return Results.Content(html, TextHtmlContentType, Encoding.UTF8);
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

    private static IResult Logout() =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = McpGatewayConventions.Approvals.CodeRoute },
            [GatewayAuthConventions.Schemes.ApprovalCookie]);

    private static async Task<IResult?> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(McpGatewayMessages.ArgumentValidation.InvalidApprovalFormToken);
        }
    }

    private static bool IsApprovalReturnUrl(string returnUrl) =>
        returnUrl.StartsWith(McpGatewayConventions.Approvals.PathPrefix + "/", StringComparison.Ordinal);

    internal static ApprovalPageData BuildApprovalPageData(ApprovalPageModel model, string? requestToken)
    {
        var challengeInfo = model.Challenge is not null
            ? new ApprovalChallengeInfo(
                model.Challenge.Id,
                model.Challenge.PlanId,
                model.Challenge.RequesterSubject,
                model.Challenge.RequesterAuthenticationType,
                model.Challenge.CreatedAtUtc,
                model.Challenge.ExpiresAtUtc,
                model.Challenge.Status)
            : null;

        var actions = new ApprovalActionUrls(
            $"{McpGatewayConventions.Approvals.PathPrefix}/{model.Challenge?.Id}/approve",
            $"{McpGatewayConventions.Approvals.PathPrefix}/{model.Challenge?.Id}/deny",
            $"{McpGatewayConventions.Approvals.PathPrefix}/{model.Challenge?.Id}/cancel",
            McpGatewayConventions.Approvals.RequestVerificationToken,
            requestToken);

        return new ApprovalPageData(model.CanDecide, model.Error, challengeInfo, model.PlanReview, actions);
    }

    internal static DecisionPageData BuildDecisionPageData(ApprovalDecisionResult result) =>
        new(result.Succeeded, result.Message);

    internal static ApprovalCodePageData BuildCodePageData(
        string? requestToken,
        string? submittedCode,
        string? error) =>
        new(
            McpGatewayConventions.Approvals.CodeRoute,
            McpGatewayConventions.Approvals.CodeFormField,
            McpGatewayConventions.Approvals.RequestVerificationToken,
            requestToken,
            submittedCode,
            error);
}
