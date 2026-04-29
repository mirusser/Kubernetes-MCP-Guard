namespace InfraGate.DevIssuer;

internal sealed record AuthorizationCode(
    string Code,
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string Resource,
    string Scope,
    DateTimeOffset ExpiresAt);
