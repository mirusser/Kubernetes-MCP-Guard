namespace InfraGate.RunProfiles;

internal sealed record class ProfileDefaults(
    GatewayProfile? Gateway,
    IdentityProviderProfile? IdentityProvider,
    ApprovalAuthorityProfile? ApprovalAuthority,
    GenericApprovalCoreProfile? GenericApprovalCore,
    HostProfile? Host,
    DownstreamAuthProfile? DownstreamAuth,
    OpenRouterProfile? OpenRouter,
    ObserverProfile? Observer,
    PlannerProfile? Planner,
    ExecutorProfile? Executor,
    AgentGuardrailsProfile? AgentGuardrails = null);
