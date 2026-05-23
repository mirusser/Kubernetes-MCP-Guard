namespace InfraGate.Observer.Contracts;

public static class AnomalyObserverConventions
{
    public const int DefaultCadenceSeconds = 60;
    public const int MinCadenceSeconds = 10;
    public const int MaxCadenceSeconds = 3600;
    public const int WallClockCapSeconds = 20;
    public const int MaxToolIterations = 8;
}
