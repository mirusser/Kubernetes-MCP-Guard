using InfraGate.Executor.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Endpoints;

internal static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapExecutorHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ExecutorConventions.HealthEndpointPath, async (
            IClientCredentialsTokenProvider tokenProvider,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Executor.Health");

            try
            {
                string token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    ExecutorLogEvents.LogHealthCheckStarting(logger);
                    return Results.Json(new { status = "starting" }, statusCode: 503);
                }

                return Results.Json(new { status = "healthy" });
            }
            catch (Exception ex)
            {
                ExecutorLogEvents.LogHealthCheckFailed(logger, ex);
                return Results.Json(new { status = "unhealthy", reason = "token_acquisition_failed" }, statusCode: 503);
            }
        });

        return endpoints;
    }
}
