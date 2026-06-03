using InfraGate.RuntimeSafety;

namespace InfraGate.Planner.Settings;

/// <summary>
/// Strongly-typed Planner configuration bound from the <c>InfraGate:Planner</c> section
/// (see <see cref="SectionName"/>). The framework binder matches property names to configuration
/// keys recursively — <see cref="ClientCredentials"/> binds automatically from
/// <c>InfraGate:Planner:ClientCredentials</c>; there is no manual env-var mapping or per-key reads.
/// </summary>
public sealed record class PlannerOptions
{
    public const string SectionName = "InfraGate:Planner";

    public string GatewayBaseUrl { get; init; } = string.Empty;
    public string ExecutorHandoffUrl { get; init; } = string.Empty;
    public int AnomalyWallClockCapSeconds { get; init; } = PlannerConventions.DefaultAnomalyWallClockCapSeconds;
    public int BatchWallClockCapSeconds { get; init; } = PlannerConventions.DefaultBatchWallClockCapSeconds;
    public int MaxToolIterations { get; init; } = PlannerConventions.DefaultMaxToolIterations;
    public string LlmProvider { get; init; } = string.Empty;
    public string LlmModel { get; init; } = string.Empty;
    public string FileSinkRoot { get; init; } = string.Empty;
    public string ObserverBaseUrl { get; init; } = string.Empty;
    public string AuditConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// OAuth client-credentials the Planner uses to authenticate its outbound MCP calls.
    /// Bound recursively from <c>InfraGate:Planner:ClientCredentials</c>; validated at startup by
    /// <c>AddClientCredentialsTokenProvider</c>.
    /// </summary>
    public ClientCredentialsTokenOptions ClientCredentials { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(GatewayBaseUrl))
        {
            throw new InvalidOperationException("GatewayBaseUrl must be configured.");
        }

        if (AnomalyWallClockCapSeconds < PlannerConventions.MinAnomalyWallClockCapSeconds ||
            AnomalyWallClockCapSeconds > PlannerConventions.MaxAnomalyWallClockCapSeconds)
        {
            throw new InvalidOperationException(
                $"AnomalyWallClockCapSeconds must be between {PlannerConventions.MinAnomalyWallClockCapSeconds} and {PlannerConventions.MaxAnomalyWallClockCapSeconds}. Configured: {AnomalyWallClockCapSeconds}.");
        }

        if (BatchWallClockCapSeconds < PlannerConventions.MinBatchWallClockCapSeconds ||
            BatchWallClockCapSeconds > PlannerConventions.MaxBatchWallClockCapSeconds)
        {
            throw new InvalidOperationException(
                $"BatchWallClockCapSeconds must be between {PlannerConventions.MinBatchWallClockCapSeconds} and {PlannerConventions.MaxBatchWallClockCapSeconds}. Configured: {BatchWallClockCapSeconds}.");
        }

        if (MaxToolIterations < PlannerConventions.MinMaxToolIterations ||
            MaxToolIterations > PlannerConventions.MaxMaxToolIterations)
        {
            throw new InvalidOperationException(
                $"MaxToolIterations must be between {PlannerConventions.MinMaxToolIterations} and {PlannerConventions.MaxMaxToolIterations}. Configured: {MaxToolIterations}.");
        }
    }

    public void ValidateProductionSafety(RuntimeMode runtimeMode)
    {
        if (runtimeMode != RuntimeMode.Production)
        {
            return;
        }

        ProductionSafetyValidator.RequireExplicitNonDemoLlmRoute(
            LlmProvider,
            LlmModel,
            PlannerConventions.ConfigurationKeys.LlmProvider,
            PlannerConventions.ConfigurationKeys.LlmModel);
        ProductionSafetyValidator.RequireHttpsMetadataEnabled(
            ClientCredentials.RequireHttpsMetadata,
            PlannerConventions.ConfigurationKeys.OAuthRequireHttpsMetadata);
        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            ClientCredentials.Authority,
            PlannerConventions.ConfigurationKeys.OAuthAuthority);
    }
}
