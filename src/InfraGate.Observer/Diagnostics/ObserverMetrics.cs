using System.Diagnostics.Metrics;

namespace InfraGate.Observer.Diagnostics;

internal static class ObserverMetrics
{
    internal const string MeterName = "InfraGate.Observer";
    internal const string MeterVersion = "1.0";

    internal const string HandoffFailedCounterName = "infragate.observer.handoff.failed";
    internal const string SinkNameTag = "SinkName";

    internal static readonly Meter Meter = new(MeterName, MeterVersion);

    internal static Counter<long> CreateHandoffFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(HandoffFailedCounterName);
    }
}
