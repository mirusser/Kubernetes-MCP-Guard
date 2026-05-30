using System.Diagnostics.Metrics;

namespace InfraGate.Executor.Diagnostics;

internal static class ExecutorMetrics
{
    internal const string MeterName = "InfraGate.Executor";
    internal const string MeterVersion = "1.0";

    internal const string WatchTimeoutCounterName = "infragate.executor.watch.timeout";
    internal const string WatchFailedCounterName = "infragate.executor.watch.failed";
    internal const string ExecuteSucceededCounterName = "infragate.executor.execute.succeeded";
    internal const string ExecuteFailedCounterName = "infragate.executor.execute.failed";
    internal const string ExecuteBlockedCounterName = "infragate.executor.execute.blocked";

    internal static readonly Meter Meter = new(MeterName, MeterVersion);

    internal static Counter<long> CreateWatchTimeoutCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(WatchTimeoutCounterName);
    }

    internal static Counter<long> CreateWatchFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(WatchFailedCounterName);
    }

    internal static Counter<long> CreateExecuteSucceededCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ExecuteSucceededCounterName);
    }

    internal static Counter<long> CreateExecuteFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ExecuteFailedCounterName);
    }

    internal static Counter<long> CreateExecuteBlockedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ExecuteBlockedCounterName);
    }
}
