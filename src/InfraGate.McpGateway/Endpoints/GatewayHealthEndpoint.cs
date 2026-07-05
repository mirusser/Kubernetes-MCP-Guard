using Npgsql;

namespace InfraGate.McpGateway.Endpoints;

internal static class GatewayHealthEndpoint
{
    public static IEndpointRouteBuilder MapGatewayHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpGatewayConventions.Health.LivenessPath, () =>
            Results.Json(new { status = "healthy" }));

        endpoints.MapGet(McpGatewayConventions.Health.ReadinessPath, async (
            NpgsqlDataSource dataSource,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    return Results.Json(new { status = "healthy" });
                }
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { status = "unhealthy", reason = "postgres_unavailable", error = ex.Message },
                    statusCode: 503);
            }
        });

        return endpoints;
    }
}
