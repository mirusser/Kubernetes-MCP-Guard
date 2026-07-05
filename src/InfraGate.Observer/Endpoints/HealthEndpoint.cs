using InfraGate.ClientCredentials;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InfraGate.Observer.Endpoints;

internal static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapObserverHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ObserverConventions.HealthEndpointPath, HealthCheckHandler);
        endpoints.MapGet(ObserverConventions.Health.LivenessPath, LivenessHandler);
        endpoints.MapGet(ObserverConventions.Health.ReadinessPath, ReadinessHandler);

        return endpoints;
    }

    private static IResult LivenessHandler() =>
        Results.Json(new { status = "healthy" });

    private static async Task<IResult> ReadinessHandler(
        IClientCredentialsTokenProvider tokenProvider,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
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
        }
        catch (Exception ex)
        {
            ObserverLogEvents.LogHealthCheckFailed(logger, ex);
            return Results.Json(
                new { status = "unhealthy", reason = "token_acquisition_failed" },
                statusCode: 503);
        }

        var dataSource = serviceProvider.GetService<NpgsqlDataSource>();
        if (dataSource is null)
        {
            // AuditConnectionString is documented as optional (docs/configuration.md);
            // omitting it is a valid deployment, not a failure — surface it distinctly
            // rather than reporting the same "healthy" a passing Postgres check would.
            return Results.Json(new { status = "healthy", postgres = "not_configured" });
        }

        try
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return Results.Json(new { status = "healthy" });
            }
        }
        catch (Exception ex)
        {
            ObserverLogEvents.LogHealthCheckFailed(logger, ex);
            return Results.Json(
                new { status = "unhealthy", reason = "postgres_unavailable", error = ex.Message },
                statusCode: 503);
        }
    }

    private static async Task<IResult> HealthCheckHandler(
        IClientCredentialsTokenProvider tokenProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
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
    }
}
