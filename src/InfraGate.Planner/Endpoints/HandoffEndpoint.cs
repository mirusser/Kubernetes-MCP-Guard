using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Endpoints;

internal static class HandoffEndpoint
{
    public static IEndpointRouteBuilder MapPlannerHandoffEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PlannerConventions.HandoffAnomaliesEndpointPath, async (
            AnomalyHandoffBatch batch,
            AnomalyBatchQueue queue,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Planner.Handoff");
            PlannerLogEvents.LogHandoffBatchReceived(logger, batch.CycleId, batch.Reports.Count);
            queue.TryEnqueue(batch);
            return Results.Accepted();
        })
        .RequireAuthorization(PlannerConventions.Policies.ObserverSender);

        return endpoints;
    }
}
