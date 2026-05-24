using InfraGate.Observer.Cycle;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Endpoints;

internal static class ObserveNowEndpoint
{
    public static IEndpointRouteBuilder MapObserverObserveNowEndpoint(
        this IEndpointRouteBuilder endpoints,
        int timeoutSeconds = ObserverConventions.ObserveNowTimeoutSeconds)
    {
        endpoints.MapPost(ObserverConventions.ObserveNowEndpointPath, async (
            IObservationCycleRunner cycleRunner,
            CycleSerialisation cycleSerialisation,
            ILoggerFactory loggerFactory,
            HttpContext context) =>
        {
            var logger = loggerFactory.CreateLogger("InfraGate.Observer.ObserveNow");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, timeoutCts.Token);

            bool acquired = false;

            try
            {
                await cycleSerialisation.AcquireForOnDemandAsync(linkedCts.Token).ConfigureAwait(false);
                acquired = true;

                ObserverLogEvents.LogObserveNowTriggered(logger);

                var result = await cycleRunner.RunAsync(linkedCts.Token).ConfigureAwait(false);

                return Results.Ok(result.Reports);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                ObserverLogEvents.LogObserveNowTimeout(logger);
                return Results.Json(new ObserveNowErrorResponse("observation timed out"), statusCode: 504);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ObserverLogEvents.LogObserveNowError(logger, ex);
                return Results.Json(new ObserveNowErrorResponse("observation failed"), statusCode: 500);
            }
            finally
            {
                if (acquired)
                {
                    cycleSerialisation.Release();
                }
            }
        });

        return endpoints;
    }
}
