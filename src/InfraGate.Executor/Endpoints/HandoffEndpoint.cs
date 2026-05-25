using InfraGate.Executor.Diagnostics;
using InfraGate.Executor.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Endpoints;

internal static class HandoffEndpoint
{
    public static IEndpointRouteBuilder MapExecutorHandoffEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ExecutorConventions.HandoffProposalsEndpointPath, (
            RemediationProposalBatch batch,
            ProposalQueue queue,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Executor.Handoff");
            ExecutorLogEvents.LogHandoffBatchReceived(logger, batch.CycleId, batch.Proposals.Count);

            if (!queue.TryEnqueueAll(batch.Proposals))
            {
                ExecutorLogEvents.LogHandoffCapacityRejected(logger, batch.CycleId, batch.Proposals.Count);
                return Results.StatusCode(429);
            }

            return Results.Accepted();
        })
        .RequireAuthorization(ExecutorConventions.Policies.PlannerSender);

        return endpoints;
    }
}
