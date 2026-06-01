using System.Diagnostics.Metrics;

namespace InfraGate.Planner.Diagnostics;

internal static class PlannerMetrics
{
    internal const string MeterName = "InfraGate.Planner";
    internal const string MeterVersion = "1.0";

    internal const string DecisionTimeoutCounterName = "infragate.planner.decision.timeout";
    internal const string ProposeFailedCounterName = "infragate.planner.propose.failed";
    internal const string HandoffSinkFailedCounterName = "infragate.planner.handoff.sink_failed";

    internal const string SinkNameTag = "sink_name";

    internal static readonly Meter Meter = new(MeterName, MeterVersion);

    internal static Counter<long> CreateDecisionTimeoutCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(DecisionTimeoutCounterName);
    }

    internal static Counter<long> CreateProposeFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ProposeFailedCounterName);
    }

    internal static Counter<long> CreateHandoffSinkFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(HandoffSinkFailedCounterName);
    }
}
