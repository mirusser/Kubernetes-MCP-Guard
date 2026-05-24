namespace InfraGate.Approvals;

public interface IDomainPlanBuilder
{
    Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct);
}
