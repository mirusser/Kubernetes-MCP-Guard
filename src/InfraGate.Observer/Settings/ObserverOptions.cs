namespace InfraGate.Observer.Settings;

/// <summary>
/// Strongly-typed Observer configuration bound from the <c>InfraGate:Observer</c> section
/// (see <see cref="SectionName"/>). The framework binder matches property names to configuration
/// keys recursively — <see cref="ClientCredentials"/> binds automatically from
/// <c>InfraGate:Observer:ClientCredentials</c>; there is no manual env-var mapping or per-key reads.
/// </summary>
public sealed record class ObserverOptions
{
    public const string SectionName = "InfraGate:Observer";

    public int CycleIntervalSeconds { get; init; } = AnomalyObserverConventions.DefaultCadenceSeconds;
    public int WallClockCapSeconds { get; init; } = AnomalyObserverConventions.WallClockCapSeconds;
    public int MaxToolIterations { get; init; } = AnomalyObserverConventions.MaxToolIterations;
    public string GatewayBaseUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedNamespaces { get; init; } = [];
    public string LlmProvider { get; init; } = string.Empty;
    public string LlmModel { get; init; } = string.Empty;
    public string LlmApiKey { get; init; } = string.Empty;
    public int DedupeSuppressionWindow { get; init; } = AnomalyObserverConventions.DefaultDedupeSuppressionWindow;
    public int DedupeResolutionThreshold { get; init; } = AnomalyObserverConventions.DefaultDedupeResolutionThreshold;
    public string FileSinkRoot { get; init; } = string.Empty;
    public string PlannerHandoffUrl { get; init; } = string.Empty;
    public string AuditConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// OAuth client-credentials the Observer uses to authenticate its outbound MCP calls.
    /// Bound recursively from <c>InfraGate:Observer:ClientCredentials</c>; validated at startup by
    /// <c>AddClientCredentialsTokenProvider</c>.
    /// </summary>
    public ClientCredentialsTokenOptions ClientCredentials { get; init; } = new();

    public void Validate()
    {
        if (CycleIntervalSeconds < AnomalyObserverConventions.MinCadenceSeconds ||
            CycleIntervalSeconds > AnomalyObserverConventions.MaxCadenceSeconds)
        {
            throw new InvalidOperationException(
                $"CycleIntervalSeconds must be between {AnomalyObserverConventions.MinCadenceSeconds} and {AnomalyObserverConventions.MaxCadenceSeconds}. Configured: {CycleIntervalSeconds}.");
        }

        if (WallClockCapSeconds < AnomalyObserverConventions.MinWallClockCapSeconds ||
            WallClockCapSeconds > AnomalyObserverConventions.MaxWallClockCapSeconds)
        {
            throw new InvalidOperationException(
                $"WallClockCapSeconds must be between {AnomalyObserverConventions.MinWallClockCapSeconds} and {AnomalyObserverConventions.MaxWallClockCapSeconds}. Configured: {WallClockCapSeconds}.");
        }

        if (MaxToolIterations < AnomalyObserverConventions.MinMaxToolIterations ||
            MaxToolIterations > AnomalyObserverConventions.MaxMaxToolIterations)
        {
            throw new InvalidOperationException(
                $"MaxToolIterations must be between {AnomalyObserverConventions.MinMaxToolIterations} and {AnomalyObserverConventions.MaxMaxToolIterations}. Configured: {MaxToolIterations}.");
        }

        if (DedupeSuppressionWindow < AnomalyObserverConventions.MinDedupeSuppressionWindow ||
            DedupeSuppressionWindow > AnomalyObserverConventions.MaxDedupeSuppressionWindow)
        {
            throw new InvalidOperationException(
                $"DedupeSuppressionWindow must be between {AnomalyObserverConventions.MinDedupeSuppressionWindow} and {AnomalyObserverConventions.MaxDedupeSuppressionWindow}. Configured: {DedupeSuppressionWindow}.");
        }

        if (DedupeResolutionThreshold < AnomalyObserverConventions.MinDedupeResolutionThreshold ||
            DedupeResolutionThreshold > AnomalyObserverConventions.MaxDedupeResolutionThreshold)
        {
            throw new InvalidOperationException(
                $"DedupeResolutionThreshold must be between {AnomalyObserverConventions.MinDedupeResolutionThreshold} and {AnomalyObserverConventions.MaxDedupeResolutionThreshold}. Configured: {DedupeResolutionThreshold}.");
        }

        if (string.IsNullOrWhiteSpace(GatewayBaseUrl))
        {
            throw new InvalidOperationException("GatewayBaseUrl must be configured.");
        }
    }
}
