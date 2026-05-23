using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Handoff;

internal sealed partial class CompositeAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly IReadOnlyList<IAnomalyHandoffSink> sinks;
    private readonly ILogger<CompositeAnomalyHandoffSink> logger;
    private readonly Counter<long>? handoffFailedCounter;

    public CompositeAnomalyHandoffSink(
        IReadOnlyList<IAnomalyHandoffSink> sinks,
        ILogger<CompositeAnomalyHandoffSink>? logger = null,
        Meter? meter = null)
    {
        this.sinks = sinks;
        this.logger = logger ?? NullLogger<CompositeAnomalyHandoffSink>.Instance;
        this.handoffFailedCounter = meter is not null
            ? ObserverMetrics.CreateHandoffFailedCounter(meter)
            : null;
    }

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        foreach (var sink in sinks)
        {
            try
            {
                await sink.PublishAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHandoffSinkFailed(logger, sink.GetType().Name, ex.Message, ex);

                if (handoffFailedCounter is not null)
                {
                    handoffFailedCounter.Add(1,
                        new KeyValuePair<string, object?>(ObserverMetrics.SinkNameTag, sink.GetType().Name));
                }
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handoff sink '{SinkName}' failed: {ErrorMessage}")]
    private static partial void LogHandoffSinkFailed(
        ILogger logger,
        string sinkName,
        string errorMessage,
        Exception ex);
}
