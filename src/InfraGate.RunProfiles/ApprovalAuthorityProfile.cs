namespace InfraGate.RunProfiles;

internal sealed record ApprovalAuthorityProfile(
    string? BaseUrl,
    string? OauthClientId,
    string? OauthCallbackPath,
    string? OauthAuthorizationEndpoint,
    string? OauthTokenEndpoint);
