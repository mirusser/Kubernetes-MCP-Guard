namespace InfraGate.McpGateway.Endpoints;

internal static class GatewayHealthEndpoint
{
    public static IEndpointRouteBuilder MapGatewayHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpGatewayConventions.Health.LivenessPath, () =>
            Results.Json(new { status = "healthy" }));

        endpoints.MapGet(McpGatewayConventions.Health.ReadinessPath, async (
            GatewayReadinessChecker readinessChecker,
            CancellationToken cancellationToken) =>
        {
            GatewayReadinessReport report = await readinessChecker.CheckAsync(cancellationToken).ConfigureAwait(false);

            var body = new
            {
                status = report.IsReady ? "healthy" : "unhealthy",
                postgres = report.PostgresHealthy ? "healthy" : "unhealthy",
                primary = report.PrimaryHealthy ? "healthy" : "unhealthy",
                secondary = new
                {
                    status = report.Secondary.Status.ToString(),
                    processGeneration = report.Secondary.ProcessGeneration,
                    catalogGeneration = report.Secondary.CatalogGeneration,
                    reason = report.Secondary.DegradedReason,
                },
            };

            return Results.Json(body, statusCode: report.IsReady ? 200 : 503);
        });

        return endpoints;
    }
}
