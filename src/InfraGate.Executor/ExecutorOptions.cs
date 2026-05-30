namespace InfraGate.Executor;

public sealed record class ExecutorOptions
{
    public string GatewayBaseUrl { get; init; } = string.Empty;
    public int ConcurrencyCap { get; init; } = ExecutorConventions.DefaultConcurrencyCap;
    public int WatchTimeoutSeconds { get; init; } = ExecutorConventions.DefaultWatchTimeoutSeconds;

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
}
