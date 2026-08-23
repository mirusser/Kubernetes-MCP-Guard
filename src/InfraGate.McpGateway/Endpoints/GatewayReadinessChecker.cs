using Npgsql;

namespace InfraGate.McpGateway.Endpoints;

/// <summary>
/// Evaluates Gateway readiness across Postgres, the mandatory primary downstream, and the
/// optional secondary downstream. Constructed once (singleton) with each dependency already
/// resolved, since all three are themselves singletons for the lifetime of the process.
/// </summary>
internal sealed class GatewayReadinessChecker(
    NpgsqlDataSource postgresDataSource,
    IDownstreamMcpClient primaryClient,
    IDownstreamMcpClient? secondaryClient,
    DownstreamToolCatalog catalog)
{
    public async Task<GatewayReadinessReport> CheckAsync(CancellationToken cancellationToken)
    {
        bool postgresHealthy = await CheckPostgresAsync(cancellationToken).ConfigureAwait(false);
        bool primaryHealthy = await CheckPrimaryAsync(cancellationToken).ConfigureAwait(false);
        SecondaryDownstreamHealth secondary = await CheckSecondaryAsync(cancellationToken).ConfigureAwait(false);

        return new GatewayReadinessReport(
            IsReady: postgresHealthy && primaryHealthy,
            PostgresHealthy: postgresHealthy,
            PrimaryHealthy: primaryHealthy,
            Secondary: secondary);
    }

    private async Task<bool> CheckPostgresAsync(CancellationToken cancellationToken)
    {
        try
        {
            NpgsqlConnection connection = await postgresDataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> CheckPrimaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await primaryClient.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<SecondaryDownstreamHealth> CheckSecondaryAsync(CancellationToken cancellationToken)
    {
        if (secondaryClient is not ISupervisedDownstreamStatus status)
        {
            return new SecondaryDownstreamHealth(SecondaryDownstreamStatus.NotConfigured, 0, 0, null);
        }

        long processGeneration = status.ProcessGeneration;
        long catalogGeneration = catalog.GetSourceGeneration(McpGatewayConventions.DownstreamSources.Secondary);

        // A restart owns the transport (reset + respawn) while in progress; probing it here would
        // race the supervisor's own recovery rather than observe it, so report from cached state.
        if (status.IsRestarting)
        {
            return new SecondaryDownstreamHealth(
                SecondaryDownstreamStatus.BackingOff, processGeneration, catalogGeneration, null);
        }

        if (catalog.GetDegradedSources().TryGetValue(
            McpGatewayConventions.DownstreamSources.Secondary, out string? degradedReason))
        {
            return new SecondaryDownstreamHealth(
                SecondaryDownstreamStatus.Degraded, processGeneration, catalogGeneration, degradedReason);
        }

        bool liveTransportOk;
        try
        {
            await secondaryClient.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            liveTransportOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transport fault here also triggers the supervisor's own restart (see
            // DownstreamProcessSupervisor.ListToolsAsync); the next check observes that as
            // BackingOff. Re-reading the generation after the probe would race that restart, so
            // report against the pre-probe snapshot taken above.
            liveTransportOk = false;
        }

        catalogGeneration = catalog.GetSourceGeneration(McpGatewayConventions.DownstreamSources.Secondary);
        bool catalogValidatedForCurrentProcess = catalogGeneration > 0 && catalogGeneration >= processGeneration;

        SecondaryDownstreamStatus resolvedStatus = liveTransportOk && catalogValidatedForCurrentProcess
            ? SecondaryDownstreamStatus.Ready
            : SecondaryDownstreamStatus.Starting;

        return new SecondaryDownstreamHealth(resolvedStatus, processGeneration, catalogGeneration, null);
    }
}
