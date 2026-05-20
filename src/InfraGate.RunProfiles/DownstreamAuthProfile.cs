namespace InfraGate.RunProfiles;

internal sealed record class DownstreamAuthProfile(
    string? Required,
    string? Authority,
    string? MetadataAddress,
    string? RequireHttpsMetadata,
    string? Audience,
    string? Scope,
    string? GatewayClientId,
    string? GatewayClientSecret);
