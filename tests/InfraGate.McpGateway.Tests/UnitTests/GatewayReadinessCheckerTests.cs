using InfraGate.McpGateway.Endpoints;
using InfraGate.McpGateway.Tests.Fakes;
using Npgsql;

namespace InfraGate.McpGateway.Tests.UnitTests;

// Postgres is exercised only via the unreachable path: this test project has no real Postgres
// fixture (see GatewayHealthEndpointTests, which uses the same unreachable connection string).
// These tests instead focus on the primary/secondary state machine, which is fully controllable
// via fakes.
public sealed class GatewayReadinessCheckerTests
{
    private static NpgsqlDataSource UnreachablePostgres() =>
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1");

    [Fact]
    public async Task CheckAsync_PostgresUnreachable_ReportsPostgresUnhealthyAndOverallNotReady()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondaryClient: null, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.False(report.PostgresHealthy);
        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task CheckAsync_PrimaryThrows_ReportsPrimaryUnhealthyAndOverallNotReady()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient { ListToolsException = new IOException("transport gone") };
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondaryClient: null, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.False(report.PrimaryHealthy);
        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task CheckAsync_NoSecondaryConfigured_ReportsNotConfigured()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondaryClient: null, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.NotConfigured, report.Secondary.Status);
    }

    [Fact]
    public async Task CheckAsync_SecondaryStarting_LiveButCatalogNotYetPublished_ReportsStarting()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1 };
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.Starting, report.Secondary.Status);
        Assert.Equal(0, report.Secondary.CatalogGeneration);
    }

    [Fact]
    public async Task CheckAsync_SecondaryLiveAndCatalogCaughtUpToProcessGeneration_ReportsReady()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1 };
        var catalog = new DownstreamToolCatalog();
        await catalog.PublishSnapshotAsync(McpGatewayConventions.DownstreamSources.Secondary, []);
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.Ready, report.Secondary.Status);
        Assert.Equal(1, report.Secondary.CatalogGeneration);
        Assert.Equal(1, report.Secondary.ProcessGeneration);
    }

    [Fact]
    public async Task CheckAsync_SecondaryCatalogStaleAfterRestart_ReportsStartingNotReady()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        // Process generation 2 (one successful restart) but the catalog has only ever
        // published generation 1 — a validated snapshot for the *current* process is still
        // pending, so this must not be reported Ready (AC (b)).
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 2 };
        var catalog = new DownstreamToolCatalog();
        await catalog.PublishSnapshotAsync(McpGatewayConventions.DownstreamSources.Secondary, []);
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.Starting, report.Secondary.Status);
    }

    [Fact]
    public async Task CheckAsync_SecondaryRestarting_ReportsBackingOff()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1, IsRestarting = true };
        var catalog = new DownstreamToolCatalog();
        await catalog.PublishSnapshotAsync(McpGatewayConventions.DownstreamSources.Secondary, []);
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.BackingOff, report.Secondary.Status);
    }

    [Fact]
    public async Task CheckAsync_SecondaryCatalogRejectedSnapshot_ReportsDegradedWithReason()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1 };
        var catalog = new DownstreamToolCatalog();
        catalog.RecordSourceDegraded(
            McpGatewayConventions.DownstreamSources.Secondary,
            "Schema drift detected for tool 'list_pods'.");
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.Degraded, report.Secondary.Status);
        Assert.Equal("Schema drift detected for tool 'list_pods'.", report.Secondary.DegradedReason);
    }

    [Fact]
    public async Task CheckAsync_SecondaryExhaustedRestartAttempts_ReportsDegradedWithExhaustedReason()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1 };
        var catalog = new DownstreamToolCatalog();
        catalog.RecordSourceDegraded(
            McpGatewayConventions.DownstreamSources.Secondary,
            McpGatewayMessages.ToolCatalog.RestartAttemptsExhausted);
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(SecondaryDownstreamStatus.Degraded, report.Secondary.Status);
        Assert.Equal(McpGatewayMessages.ToolCatalog.RestartAttemptsExhausted, report.Secondary.DegradedReason);
    }

    [Fact]
    public async Task CheckAsync_SecondaryDegradedReasonNeverLeaksRawExceptionText()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient();
        var secondary = new FakeSupervisedDownstreamMcpClient
        {
            ProcessGeneration = 1,
            ListToolsException = new IOException("connection to 10.0.0.7:6443 refused, token 'super-secret'"),
        };
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondary, catalog);

        GatewayReadinessReport report = await checker.CheckAsync(CancellationToken.None);

        Assert.DoesNotContain("10.0.0.7", report.Secondary.DegradedReason ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", report.Secondary.DegradedReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_ShuttingDown_PropagatesCancellationInsteadOfReportingUnhealthy()
    {
        using NpgsqlDataSource postgres = UnreachablePostgres();
        var primary = new FakeSupervisedDownstreamMcpClient
        {
            ListToolsException = new OperationCanceledException("gateway shutting down"),
        };
        var catalog = new DownstreamToolCatalog();
        var checker = new GatewayReadinessChecker(postgres, primary, secondaryClient: null, catalog);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => checker.CheckAsync(CancellationToken.None));
    }
}
