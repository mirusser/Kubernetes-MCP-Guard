namespace InfraGate.RunProfiles;

internal sealed record class ApprovalAuthorityProfile(
    string? BaseUrl,
    string? OauthClientId,
    string? OauthCallbackPath,
    string? OauthAuthorizationEndpoint,
    string? OauthTokenEndpoint);
