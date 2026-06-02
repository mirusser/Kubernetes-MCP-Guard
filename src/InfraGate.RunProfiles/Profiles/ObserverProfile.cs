namespace InfraGate.RunProfiles;

internal sealed record class ObserverProfile(
    string? AspnetcoreUrls,
    string? GatewayBaseUrl,
    string? OAuthAuthority,
    string? ClientId,
    string? ClientSecret,
    string? Scope,
    string? LlmProvider,
    string? LlmModel,
    string? LlmApiKey,
    string? CycleCadenceSeconds,
    string? CycleWallClockCapSeconds,
    string? MaxToolIterations,
    string? FileSinkRoot,
    string? PlannerHandoffUrl,
    string? ObserverHostPath,
    string? AuditConnectionString,
    IReadOnlyList<string>? AllowedNamespaces);
