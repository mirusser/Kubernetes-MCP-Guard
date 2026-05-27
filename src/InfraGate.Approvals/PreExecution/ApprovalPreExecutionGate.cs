using InfraGate.Approvals.AuditPayloads;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Execution;

using InfraGate.Approvals;
namespace InfraGate.Approvals.PreExecution;

public sealed class ApprovalPreExecutionGate(
    IApprovalPlanWorkflow approvalPlans,
    IApprovalAuditPublisher? auditPublisher = null) : IApprovalPreExecutionGate
{
    private readonly IApprovalAuditPublisher auditPublisher = auditPublisher ?? NoOpApprovalAuditPublisher.Instance;

    // The profile's 8 sequential pre-execution gates are implemented as two ownership buckets:
    // Bucket 1 — generic core (gates 1–6: grant validity, plan window, grant expiry, authorization,
    //   intent digest, review digest, reuse policy): owned by PostgresApprovalPersistence.GetGrantedPlanAsync,
    //   backed by ApprovalGrantValidation.Validate.
    // Bucket 2 — domain adapter (gates 7–8: freshness policy, domain policy checks): owned by
    //   domainExecutor.CheckPreExecutionAsync.
    public async Task<PreExecutionGateResult> EvaluateAsync(
        string planId,
        IDomainPlanExecutor domainExecutor,
        CancellationToken cancellationToken)
    {
        var granted = await approvalPlans.GetGrantedPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!granted.IsGranted || granted.Envelope is null || granted.Grant is null)
        {
            return PreExecutionGateResult.Blocked(planId, granted.Message, granted.ReasonCode);
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
