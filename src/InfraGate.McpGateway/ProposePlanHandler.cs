using System.Diagnostics.Metrics;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Email;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal sealed class ProposePlanHandler( // NOSONAR:S107 - Handler composes explicit gateway, approval, notification, and request-context seams.
    IDomainAdapter domainAdapter,
    IApprovalPlanWorkflow approvalPlans,
    IGatewayApprovalService approvals,
    IApprovalAccessCodeStore accessCodes,
    IApprovalEmailSender emailSender,
    McpGatewayOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ProposePlanHandler> logger) : IProposePlanHandler
{
    private static readonly Meter Meter = new("InfraGate.McpGateway", "1.0");
    private static readonly Counter<long> EmailFailedCounter =
        Meter.CreateCounter<long>("infragate.gateway.email.failed");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> AllowedOperations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            KubernetesAdapterConventions.MutationTools.ScaleDeployment,
            KubernetesAdapterConventions.MutationTools.SetDeploymentImage,
        };

    public async Task<CallToolResult> ProposeAsync(
        string operationType,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (!AllowedOperations.Contains(operationType))
        {
            return ErrorResult(
                $"Refused: operationType must be one of: {string.Join(", ", AllowedOperations.OrderBy(static op => op, StringComparer.Ordinal))}.");
        }

        var user = httpContextAccessor.HttpContext?.User;
        var identity = GatewayAuditIdentityResolver.Resolve(user);
        if (string.IsNullOrWhiteSpace(identity.Subject))
        {
            return ErrorResult("Refused: propose_plan requires an authenticated OAuth subject.");
        }

        var planResult = await domainAdapter.BuildAsync(
            operationType,
            arguments,
            new PlanRequester(identity.Subject, GatewayAuthConventions.Audit.OAuthAuthenticationType),
            ApprovalPolicy.OperatorApproval(options.OperatorGroup),
            cancellationToken).ConfigureAwait(false);

        if (!planResult.Succeeded || planResult.Envelope is null)
        {
            return ErrorResult(planResult.Message);
        }

        await approvalPlans.CreatePlanAsync(
            planResult.Envelope,
            planResult.TargetNamespace,
            cancellationToken).ConfigureAwait(false);

        var gate = await approvals.EnsureApprovedOrCreateChallengeAsync(
            planResult.PlanId,
            cancellationToken).ConfigureAwait(false);
        if (gate.Status is not ApprovalGateStatus.ApprovalRequired ||
            gate.ChallengeId is null ||
            gate.ExpiresAtUtc is null)
        {
            return ErrorResult(gate.Message);
        }

        var ttl = gate.ExpiresAtUtc.Value - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromSeconds(1);
        }

        var code = await accessCodes.GenerateAsync(gate.ChallengeId, ttl, cancellationToken).ConfigureAwait(false);
        string approvalUrl = CreateCodeUrl();
        bool emailSent = await TrySendEmailAsync(
            planResult.PlanId,
            CreatePlanSummary(operationType, planResult.TargetNamespace),
            code,
            approvalUrl,
            cancellationToken).ConfigureAwait(false);

        return TextResult(JsonSerializer.Serialize(new
        {
            planId = planResult.PlanId,
            accessCodeSent = emailSent,
            codeExpiresAt = code.ExpiresAtUtc,
            approvalUrl
        }, JsonOptions));
    }

    internal static bool IsSupportedOperation(string operationType) =>
        AllowedOperations.Contains(operationType);

    private async Task<bool> TrySendEmailAsync(
        string planId,
        string planSummary,
        ApprovalAccessCode accessCode,
        string approvalUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OperatorEmail))
        {
            EmailFailedCounter.Add(1);
            logger.LogWarning(
                "Approval email for plan '{PlanId}' was not sent because operator email is not configured.",
                planId);
            return false;
        }

        var body = ApprovalEmailRenderer.RenderPlaintext(new ApprovalEmailTemplateData(
            planId,
            planSummary,
            accessCode.Code,
            approvalUrl,
            accessCode.ExpiresAtUtc));
        var content = new ApprovalEmailContent(
            options.OperatorEmail,
            $"InfraGate approval requested for {planId}",
            body);

        try
        {
            await emailSender.SendAsync(content, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmailFailedCounter.Add(1);
            logger.LogWarning(ex, "Approval email for plan '{PlanId}' failed.", planId);
            return false;
        }
    }

    private string CreateCodeUrl()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(options.ApprovalBaseUrl)
            ? options.ApprovalBaseUrl
            : RequestBaseUrl();

        return $"{baseUrl.TrimEnd('/')}{McpGatewayConventions.Approvals.CodeRoute}";
    }

    private string RequestBaseUrl()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        return request is null
            ? McpGatewayOptions.DefaultUrl
            : $"{request.Scheme}://{request.Host}{request.PathBase}";
    }

    private static string CreatePlanSummary(string operationType, string targetNamespace) =>
        $"{operationType} in namespace '{targetNamespace}'.";

    private static CallToolResult TextResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };
}
