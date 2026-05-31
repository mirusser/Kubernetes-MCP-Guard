using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;

namespace InfraGate.Planner.Endpoints;

internal static class HandoffEndpoint
{
    public static IEndpointRouteBuilder MapPlannerHandoffEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PlannerConventions.HandoffAnomaliesEndpointPath, async (
            AnomalyHandoffBatch batch,
            AnomalyBatchQueue queue,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Planner.Handoff");
            PlannerLogEvents.LogHandoffBatchReceived(logger, batch.CycleId, batch.Reports.Count);

            var auditOutbox = httpContext.RequestServices.GetService<IPlannerAuditOutbox>();
            if (auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.HandoffReceived,
                        Payload: new
                        {
                            cycleId = batch.CycleId,
                            anomalyIds = batch.Reports.Select(r => r.AnomalyId).ToArray(),
                            count = batch.Reports.Count,
                        },
                        ActorSubject: "service:observer",
                        Outcome: "received"),
                    cancellationToken).ConfigureAwait(false);
            }

            queue.TryEnqueue(batch);
            return Results.Accepted();
        })
        .RequireAuthorization(PlannerConventions.Policies.ObserverSender);

        return endpoints;
    }
}
