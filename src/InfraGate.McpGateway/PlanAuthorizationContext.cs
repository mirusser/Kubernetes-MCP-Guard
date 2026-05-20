using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed record class PlanAuthorizationContext(string RequesterSubject, string ActorSubject) : IAuthorizationContext;
