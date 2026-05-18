namespace InfraGate.Approvals;

public sealed class ApprovalPreExecutionGate(ApprovalStore approvalStore)
{
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

        var domainResult = await domainExecutor.CheckPreExecutionAsync(granted.Envelope, cancellationToken)
            .ConfigureAwait(false);

        return domainResult.IsSuccessful
            ? PreExecutionGateResult.Passed(granted.Envelope, granted.Grant)
            : PreExecutionGateResult.Blocked(domainResult, planId);
    }
}
