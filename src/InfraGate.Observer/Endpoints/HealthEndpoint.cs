using InfraGate.ClientCredentials;
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
                    logger.LogWarning("Health check: token has not been acquired yet");
                    return Results.Json(new { status = "starting" }, statusCode: 503);
                }

                return Results.Json(new { status = "healthy" });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Health check: token acquisition failed");
                return Results.Json(new { status = "unhealthy", reason = "token_acquisition_failed" }, statusCode: 503);
            }
        });

        return endpoints;
    }
}
