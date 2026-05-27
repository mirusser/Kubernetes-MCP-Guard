namespace InfraGate.RunProfiles;

internal sealed record class IdentityProviderProfile(
    string? RealmImport,
    string? Authority,
    string? MetadataAddress,
    string? Resource,
    string? Scope,
    string? RequireHttpsMetadata);
