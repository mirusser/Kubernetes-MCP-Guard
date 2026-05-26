using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Handoff;

internal sealed class LoggingRemediationProposalSink(ILogger<LoggingRemediationProposalSink> logger) : IRemediationProposalSink
{

    public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
    {
        foreach (var proposal in batch.Proposals)
        {
            PlannerLogEvents.LogRemediationProposal(
                logger,
                batch.CycleId,
                proposal.AnomalyId,
                proposal.PlanId,
                proposal.ProposedAt);
        }

        return Task.CompletedTask;
    }
}
