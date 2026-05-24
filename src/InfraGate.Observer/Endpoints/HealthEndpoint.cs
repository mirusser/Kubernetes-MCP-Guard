using InfraGate.ClientCredentials;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Endpoints;

internal static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapObserverHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ObserverConventions.HealthEndpointPath, async (IClientCredentialsTokenProvider tokenProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Observer.Health");

            try
            {
                string token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    ObserverLogEvents.LogHealthCheckStarting(logger);
                    return Results.Json(new { status = "starting" }, statusCode: 503);
                }

                return Results.Json(new { status = "healthy" });
            }
            catch (Exception ex)
            {
                ObserverLogEvents.LogHealthCheckFailed(logger, ex);
                return Results.Json(new { status = "unhealthy", reason = "token_acquisition_failed" }, statusCode: 503);
            }
        });

        return endpoints;
    }
}
