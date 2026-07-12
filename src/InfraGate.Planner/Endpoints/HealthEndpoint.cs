using InfraGate.ClientCredentials;
using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InfraGate.Planner.Endpoints;

internal static class HealthEndpoint
{
    private const string HealthyStatus = "healthy";

    public static IEndpointRouteBuilder MapPlannerHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PlannerConventions.HealthEndpointPath, HealthCheckHandler);
        endpoints.MapGet(PlannerConventions.Health.LivenessPath, LivenessHandler);
        endpoints.MapGet(PlannerConventions.Health.ReadinessPath, ReadinessHandler);

        return endpoints;
    }

    private static IResult LivenessHandler() =>
        Results.Json(new { status = HealthyStatus });

    private static async Task<IResult> ReadinessHandler(
        IClientCredentialsTokenProvider tokenProvider,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
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
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogHealthCheckFailed(logger, ex);
            return Results.Json(
                new { status = "unhealthy", reason = "token_acquisition_failed" },
                statusCode: 503);
        }

        var dataSource = serviceProvider.GetService<NpgsqlDataSource>();
        if (dataSource is null)
        {
            // AuditConnectionString is documented as optional (docs/configuration.md) —
            // omitting it is a valid deployment, not a failure. Surface it distinctly
            // rather than reporting the same "healthy" a passing Postgres check would.
            return Results.Json(new { status = HealthyStatus, postgres = "not_configured" });
        }

        try
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return Results.Json(new { status = HealthyStatus });
            }
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogHealthCheckFailed(logger, ex);
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
        var logger = loggerFactory.CreateLogger("InfraGate.Planner.Health");

        try
        {
            string token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                PlannerLogEvents.LogHealthCheckStarting(logger);
                return Results.Json(new { status = "starting" }, statusCode: 503);
            }

            return Results.Json(new { status = HealthyStatus });
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogHealthCheckFailed(logger, ex);
            return Results.Json(new { status = "unhealthy", reason = "token_acquisition_failed" }, statusCode: 503);
        }
    }
}
