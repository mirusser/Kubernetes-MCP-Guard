namespace InfraGate.RunProfiles;

internal sealed record GatewayProfile(
    string? AspnetcoreUrls,
    string? DownstreamAssembly,
    string? GuardAuditRoot);
