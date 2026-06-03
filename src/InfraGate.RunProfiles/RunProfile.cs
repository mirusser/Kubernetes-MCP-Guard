namespace InfraGate.RunProfiles;

internal sealed record class RunProfile(
    string Name,
    string Kind,
    string? RuntimeMode,
    GatewayProfile? Gateway,
    IdentityProviderProfile? IdentityProvider,
    ApprovalAuthorityProfile? ApprovalAuthority,
    GenericApprovalCoreProfile? GenericApprovalCore,
    IReadOnlyList<DomainAdapterProfile> DomainAdapters,
    HostProfile? Host,
    DownstreamAuthProfile? DownstreamAuth,
    OpenRouterProfile? OpenRouter,
    ObserverProfile? Observer,
    PlannerProfile? Planner,
    ExecutorProfile? Executor,
    AgentGuardrailsProfile? AgentGuardrails = null);
