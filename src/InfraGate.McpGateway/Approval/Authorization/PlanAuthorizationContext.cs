using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.PreExecution;

namespace InfraGate.McpGateway;

public sealed record class PlanAuthorizationContext(
    string RequesterSubject,
    string ActorSubject,
    ApprovalPolicy? Policy = null,
    IReadOnlySet<string>? Groups = null) : IAuthorizationContext
{
    public ApprovalPolicy ApprovalPolicy { get; init; } = Policy ?? ApprovalPolicy.SameSubject();

    public IReadOnlySet<string> ActorGroups { get; init; } =
        Groups ?? new HashSet<string>(StringComparer.Ordinal);
}
