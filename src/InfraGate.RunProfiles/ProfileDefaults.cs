namespace InfraGate.RunProfiles;

internal sealed record class ProfileDefaults(
    GatewayProfile? Gateway,
    IdentityProviderProfile? IdentityProvider,
    ApprovalAuthorityProfile? ApprovalAuthority,
    GenericApprovalCoreProfile? GenericApprovalCore,
    HostProfile? Host,
    DownstreamAuthProfile? DownstreamAuth,
    ObserverProfile? Observer);
