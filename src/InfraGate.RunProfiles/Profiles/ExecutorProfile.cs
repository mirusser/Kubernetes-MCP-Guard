namespace InfraGate.RunProfiles;

internal sealed record class ExecutorProfile(
    string? AspnetcoreUrls,
    string? GatewayBaseUrl,
    string? ClientId,
    string? ClientSecret,
    string? OAuthAuthority,
    string? OAuthScope,
    string? ConcurrencyCap,
    string? WatchTimeoutSeconds,
    string? ExecutorHostPath);
