namespace InfraGate.RunProfiles;

internal sealed record class IdentityProviderProfile(
    string? RealmImport,
    string? Authority,
    string? MetadataAddress,
    string? Resource,
    string? Scope,
    string? RequireHttpsMetadata,
    string? TokenIntrospectionEnabled = null,
    string? TokenIntrospectionEndpoint = null,
    string? TokenIntrospectionClientId = null,
    string? TokenIntrospectionClientSecret = null,
    string? TokenIntrospectionCacheSeconds = null,
    string? MaxAcceptedAccessTokenLifetimeSeconds = null);
