namespace InfraGate.RunProfiles;

internal sealed record RunProfile(
    string Name,
    string Kind,
    string? RuntimeMode,
    GenericApprovalCoreProfile? GenericApprovalCore,
    IReadOnlyList<DomainAdapterProfile> DomainAdapters);
