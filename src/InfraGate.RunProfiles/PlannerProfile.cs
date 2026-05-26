namespace InfraGate.RunProfiles;

internal sealed record class PlannerProfile(
    string? AspnetcoreUrls,
    string? GatewayBaseUrl,
    string? ExecutorHandoffUrl,
    string? TokenEndpoint,
    string? ClientId,
    string? ClientSecret,
    string? OAuthAuthority,
    string? OAuthScope,
    string? LlmProvider,
    string? LlmModel,
    string? LlmApiKey,
    string? AnomalyWallClockCapSeconds,
    string? BatchWallClockCapSeconds,
    string? MaxToolIterations,
    string? FileSinkRoot,
    string? PlannerHostPath);
