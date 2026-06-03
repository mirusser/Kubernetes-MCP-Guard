using InfraGate.RuntimeSafety;

namespace InfraGate.Executor.Settings;

/// <summary>
/// Strongly-typed Executor configuration bound from the <c>InfraGate:Executor</c> section
/// (see <see cref="ExecutorConventions.SectionName"/>). The framework binder matches property
/// names to configuration keys recursively, so nested options such as <see cref="ClientCredentials"/>
/// bind automatically — there is no manual env-var mapping or per-key reads.
/// </summary>
public sealed record class ExecutorOptions
{
    public string GatewayBaseUrl { get; init; } = string.Empty;

    public int ConcurrencyCap { get; init; } = ExecutorConventions.DefaultConcurrencyCap;

    public int WatchTimeoutSeconds { get; init; } = ExecutorConventions.DefaultWatchTimeoutSeconds;

    /// <summary>
    /// OAuth client-credentials the Executor uses to authenticate its outbound MCP calls to the Gateway.
    /// Bound recursively from <c>InfraGate:Executor:ClientCredentials</c>; validated at startup by
    /// <c>AddClientCredentialsTokenProvider</c>.
    /// </summary>
    public ClientCredentialsTokenOptions ClientCredentials { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(GatewayBaseUrl))
        {
            throw new InvalidOperationException("GatewayBaseUrl must be configured.");
        }

        if (ConcurrencyCap < ExecutorConventions.MinConcurrencyCap ||
            ConcurrencyCap > ExecutorConventions.MaxConcurrencyCap)
        {
            throw new InvalidOperationException(
                $"ConcurrencyCap must be between {ExecutorConventions.MinConcurrencyCap} and {ExecutorConventions.MaxConcurrencyCap}. Configured: {ConcurrencyCap}.");
        }

        if (WatchTimeoutSeconds < ExecutorConventions.MinWatchTimeoutSeconds ||
            WatchTimeoutSeconds > ExecutorConventions.MaxWatchTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"WatchTimeoutSeconds must be between {ExecutorConventions.MinWatchTimeoutSeconds} and {ExecutorConventions.MaxWatchTimeoutSeconds}. Configured: {WatchTimeoutSeconds}.");
        }
    }

    public void ValidateProductionSafety(RuntimeMode runtimeMode)
    {
        if (runtimeMode != RuntimeMode.Production)
        {
            return;
        }

        ProductionSafetyValidator.RequireHttpsMetadataEnabled(
            ClientCredentials.RequireHttpsMetadata,
            ExecutorConventions.ConfigurationKeys.OAuthRequireHttpsMetadata);
        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            ClientCredentials.Authority,
            ExecutorConventions.ConfigurationKeys.OAuthAuthority);
    }
}
