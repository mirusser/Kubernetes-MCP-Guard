using InfraGate.Approvals.Plan;
namespace InfraGate.Approvals.PreExecution;

public interface IAuthorizationContext
{
    string RequesterSubject { get; }
    string ActorSubject { get; }
    ApprovalPolicy ApprovalPolicy { get; }
    IReadOnlySet<string> ActorGroups { get; }
}
