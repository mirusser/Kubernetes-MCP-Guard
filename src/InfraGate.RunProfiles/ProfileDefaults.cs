namespace InfraGate.RunProfiles;

internal sealed record ProfileDefaults(
    GatewayProfile? Gateway,
    IdentityProviderProfile? IdentityProvider,
    ApprovalAuthorityProfile? ApprovalAuthority,
    GenericApprovalCoreProfile? GenericApprovalCore,
    HostProfile? Host);
