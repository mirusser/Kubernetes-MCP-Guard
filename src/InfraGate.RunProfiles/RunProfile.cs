namespace InfraGate.RunProfiles;

internal sealed record RunProfile(
    string Name,
    string Kind,
    string? RuntimeMode,
    GatewayProfile? Gateway,
    IdentityProviderProfile? IdentityProvider,
    ApprovalAuthorityProfile? ApprovalAuthority,
    GenericApprovalCoreProfile? GenericApprovalCore,
    IReadOnlyList<DomainAdapterProfile> DomainAdapters,
    HostProfile? Host,
    DownstreamAuthProfile? DownstreamAuth);
