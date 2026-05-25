using InfraGate.Planner.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Endpoints;

internal static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapPlannerHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PlannerConventions.HealthEndpointPath, async (
            IClientCredentialsTokenProvider tokenProvider,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Planner.Health");

            try
            {
                string token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    PlannerLogEvents.LogHealthCheckStarting(logger);
                    return Results.Json(new { status = "starting" }, statusCode: 503);
                }

                return Results.Json(new { status = "healthy" });
            }
            catch (Exception ex)
            {
                PlannerLogEvents.LogHealthCheckFailed(logger, ex);
                return Results.Json(new { status = "unhealthy", reason = "token_acquisition_failed" }, statusCode: 503);
            }
        });

        return endpoints;
    }
}
