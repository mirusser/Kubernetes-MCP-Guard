namespace InfraGate.Observer;

public sealed record class ObserverOptions
{
    public int CycleIntervalSeconds { get; init; } = AnomalyObserverConventions.DefaultCadenceSeconds;
    public int WallClockCapSeconds { get; init; } = AnomalyObserverConventions.WallClockCapSeconds;
    public int MaxToolIterations { get; init; } = AnomalyObserverConventions.MaxToolIterations;
    public string GatewayBaseUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedNamespaces { get; init; } = Array.Empty<string>();

    public void Validate()
    {
        if (CycleIntervalSeconds < AnomalyObserverConventions.MinCadenceSeconds ||
            CycleIntervalSeconds > AnomalyObserverConventions.MaxCadenceSeconds)
        {
            throw new InvalidOperationException(
                $"CycleIntervalSeconds must be between {AnomalyObserverConventions.MinCadenceSeconds} " +
                $"and {AnomalyObserverConventions.MaxCadenceSeconds}. " +
                $"Configured: {CycleIntervalSeconds}.");
        }

        if (WallClockCapSeconds < AnomalyObserverConventions.MinWallClockCapSeconds ||
            WallClockCapSeconds > AnomalyObserverConventions.MaxWallClockCapSeconds)
        {
            throw new InvalidOperationException(
                $"WallClockCapSeconds must be between {AnomalyObserverConventions.MinWallClockCapSeconds} " +
                $"and {AnomalyObserverConventions.MaxWallClockCapSeconds}. " +
                $"Configured: {WallClockCapSeconds}.");
        }

        if (MaxToolIterations < AnomalyObserverConventions.MinMaxToolIterations ||
            MaxToolIterations > AnomalyObserverConventions.MaxMaxToolIterations)
        {
            throw new InvalidOperationException(
                $"MaxToolIterations must be between {AnomalyObserverConventions.MinMaxToolIterations} " +
                $"and {AnomalyObserverConventions.MaxMaxToolIterations}. " +
                $"Configured: {MaxToolIterations}.");
        }

        if (string.IsNullOrWhiteSpace(GatewayBaseUrl))
        {
            throw new InvalidOperationException(
                $"GatewayBaseUrl must be configured.");
        }
    }
}
