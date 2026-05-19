using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed record PlanAuthorizationContext(string RequesterSubject, string ActorSubject) : IAuthorizationContext;
