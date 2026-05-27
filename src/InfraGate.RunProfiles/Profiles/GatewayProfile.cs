namespace InfraGate.RunProfiles;

internal sealed record class GatewayProfile(
    string? AspnetcoreUrls,
    string? DownstreamAssembly,
    string? GuardAuditRoot);
