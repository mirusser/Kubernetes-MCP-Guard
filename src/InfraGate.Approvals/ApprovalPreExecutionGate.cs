using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed class ApprovalPreExecutionGate(
    ApprovalStore approvalStore,
    IApprovalAuditPublisher? auditPublisher = null)
{
    private readonly IApprovalAuditPublisher auditPublisher = auditPublisher ?? NoOpApprovalAuditPublisher.Instance;

    public async Task<PreExecutionGateResult> EvaluateAsync(
        string planId,
        IDomainPlanExecutor domainExecutor,
        CancellationToken cancellationToken)
    {
        var granted = await approvalStore.GetGrantedPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!granted.IsGranted || granted.Envelope is null || granted.Grant is null)
        {
            return PreExecutionGateResult.Blocked(planId, granted.Message);
        }

        await auditPublisher.PublishAsync(
            new PlanAudit(
                ApprovalConventions.AuditEvents.PreExecutionGrantValidated,
                new PreExecutionGrantValidatedPayload(
                    granted.Envelope.Id,
                    granted.Grant.Id,
                    granted.Grant.SourceChallengeId,
                    granted.Grant.RequesterSubject,
                    granted.Grant.ApproverSubject,
                    granted.Grant.IntentDigest,
                    granted.Grant.ReviewDigest,
                    granted.Grant.ApprovalPolicy,
                    granted.Grant.ExecutionReusePolicy,
                    granted.Grant.ExpiresAtUtc)),
            cancellationToken).ConfigureAwait(false);

        var domainResult = await domainExecutor.CheckPreExecutionAsync(granted.Envelope, cancellationToken)
            .ConfigureAwait(false);

        return domainResult.IsSuccessful
            ? PreExecutionGateResult.Passed(granted.Envelope, granted.Grant)
            : PreExecutionGateResult.Blocked(domainResult, planId);
    }
}
