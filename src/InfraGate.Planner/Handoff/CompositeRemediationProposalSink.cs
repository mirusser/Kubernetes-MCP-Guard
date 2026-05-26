using System.Diagnostics.Metrics;
using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Handoff;

internal sealed class CompositeRemediationProposalSink : IRemediationProposalSink
{
    private readonly IReadOnlyList<IRemediationProposalSink> sinks;
    private readonly ILogger<CompositeRemediationProposalSink> logger;
    private readonly Counter<long> sinkFailedCounter;

    public CompositeRemediationProposalSink(
        IReadOnlyList<IRemediationProposalSink> sinks,
        ILogger<CompositeRemediationProposalSink>? logger = null,
        Meter? meter = null)
    {
        this.sinks = sinks;
        this.logger = logger ?? NullLogger<CompositeRemediationProposalSink>.Instance;
        this.sinkFailedCounter = PlannerMetrics.CreateHandoffSinkFailedCounter(meter);
    }

    public async Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
    {
        foreach (var sink in sinks)
        {
            try
            {
                await sink.PublishAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PlannerLogEvents.LogHandoffSinkFailed(logger, sink.GetType().Name, ex.Message, ex);

                sinkFailedCounter.Add(1,
                    new KeyValuePair<string, object?>(PlannerMetrics.SinkNameTag, sink.GetType().Name));
            }
        }
    }
}
