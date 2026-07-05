using InfraGate.Executor.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Endpoints;

internal static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapExecutorHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ExecutorConventions.HealthEndpointPath, HealthCheckHandler);
        endpoints.MapGet(ExecutorConventions.Health.LivenessPath, LivenessHandler);
        endpoints.MapGet(ExecutorConventions.Health.ReadinessPath, ReadinessHandler);

        return endpoints;
    }

    private static IResult LivenessHandler() =>
        Results.Json(new { status = "healthy" });

    private static async Task<IResult> ReadinessHandler(
        IClientCredentialsTokenProvider tokenProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
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
    }

    private static async Task<IResult> HealthCheckHandler(
        IClientCredentialsTokenProvider tokenProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
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
    }
}
