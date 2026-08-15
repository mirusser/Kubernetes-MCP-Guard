using System.Diagnostics;
using System.Diagnostics.Metrics;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.McpGateway.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

/// <summary>
/// Exercises <see cref="DownstreamProcessSupervisor"/> against a real, controllable stdio MCP
/// process (<c>InfraGate.McpGateway.Tests.ProcessFixture</c>) rather than a mocking package, per
/// the Task 12 hardening plan: unexpected exit, a broken transport, concurrent faults, backoff
/// bounds, successful recovery, and clean cancellation on shutdown.
/// </summary>
public sealed class DownstreamProcessSupervisorTests
{
    [Fact]
    public async Task Restart_SingleFlightUnderConcurrentFaults_RecoversWithOneRestart()
    {
        var recordedRestarts = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamRestartCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recordedRestarts.Add(new Measurement<long>(value, tags)));
        listener.Start();

        string controlDir = ControlFile.CreateDirectory();
        await using SupervisorHarness harness = CreateHarness(
            controlDir,
            minBackoff: TimeSpan.FromMilliseconds(30),
            maxBackoff: TimeSpan.FromMilliseconds(150),
            maxAttempts: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IReadOnlyList<DownstreamTool> initialTools = await harness.Supervisor.ListToolsAsync(cts.Token);
        Assert.Contains(initialTools, tool => tool.Name == "ping");
        Assert.Equal(1, ControlFile.ReadSpawnCount(controlDir));
        Assert.Equal(1, harness.Supervisor.ProcessGeneration);

        ControlFile.WriteCommand(controlDir, "crash");
        await Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider.System, cts.Token);

        Exception?[] faults = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Record.ExceptionAsync(() => harness.Supervisor.ListToolsAsync(cts.Token))));
        Assert.Contains(faults, fault => fault is not null);

        await WaitUntilAsync(
            () => harness.Dispatcher.RegenerateCallCount >= 1,
            TimeSpan.FromSeconds(10),
            "a single restart should recover the crashed downstream");

        // Single-flight: five concurrent faults must still only trigger one respawn, one
        // generation bump, and one dispatcher notification -- never one per faulted caller.
        Assert.Equal(1, harness.Dispatcher.RegenerateCallCount);
        Assert.Equal(McpGatewayConventions.DownstreamSources.Secondary, harness.Dispatcher.LastRegeneratedSourceId);
        Assert.Equal(2, harness.Supervisor.ProcessGeneration);
        Assert.Equal(2, ControlFile.ReadSpawnCount(controlDir));
        Assert.Empty(harness.Catalog.GetDegradedSources());

        IReadOnlyList<DownstreamTool> toolsAfterRestart = await harness.Supervisor.ListToolsAsync(cts.Token);
        Assert.Contains(toolsAfterRestart, tool => tool.Name == "ping");

        Assert.Contains(recordedRestarts, m =>
            (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Source) == McpGatewayConventions.DownstreamSources.Secondary &&
            (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Outcome) == McpGatewayConventions.Telemetry.Outcomes.RestartSucceeded);
    }

    [Fact]
    public async Task Restart_UnexpectedGracefulExit_TriggersRecovery()
    {
        string controlDir = ControlFile.CreateDirectory();
        await using SupervisorHarness harness = CreateHarness(
            controlDir,
            minBackoff: TimeSpan.FromMilliseconds(30),
            maxBackoff: TimeSpan.FromMilliseconds(150),
            maxAttempts: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await harness.Supervisor.ListToolsAsync(cts.Token);
        Assert.Equal(1, ControlFile.ReadSpawnCount(controlDir));

        // "exit" is a graceful Environment.Exit(0) -- distinct from a crash/broken pipe, but the
        // supervisor never asked for it, so it must still be treated as an unexpected exit.
        ControlFile.WriteCommand(controlDir, "exit");
        await Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider.System, cts.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Supervisor.ListToolsAsync(cts.Token));

        await WaitUntilAsync(
            () => harness.Dispatcher.RegenerateCallCount >= 1,
            TimeSpan.FromSeconds(10),
            "a graceful unexpected exit should still trigger a supervised restart");

        Assert.Equal(2, harness.Supervisor.ProcessGeneration);
        Assert.Equal(2, ControlFile.ReadSpawnCount(controlDir));
    }

    [Fact]
    public async Task Restart_ExhaustedAttempts_LeavesSourceDegradedWithoutThrowing()
    {
        var recordedRestarts = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamRestartCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recordedRestarts.Add(new Measurement<long>(value, tags)));
        listener.Start();

        string controlDir = ControlFile.CreateDirectory();
        const int maxAttempts = 3;
        await using SupervisorHarness harness = CreateHarness(
            controlDir,
            minBackoff: TimeSpan.FromMilliseconds(20),
            maxBackoff: TimeSpan.FromMilliseconds(60),
            maxAttempts);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await harness.Supervisor.ListToolsAsync(cts.Token);
        Assert.Equal(1, ControlFile.ReadSpawnCount(controlDir));

        // Every respawn attempt will immediately exit(17) before starting the MCP transport, so
        // the whole restart loop is guaranteed to exhaust.
        ControlFile.SetFailStartupCount(controlDir, maxAttempts + 5);
        ControlFile.WriteCommand(controlDir, "crash");
        await Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider.System, cts.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Supervisor.ListToolsAsync(cts.Token));

        await WaitUntilAsync(
            () => harness.Catalog.GetDegradedSources().ContainsKey(McpGatewayConventions.DownstreamSources.Secondary),
            TimeSpan.FromSeconds(10),
            "restart attempts should exhaust and mark the secondary source degraded");

        Assert.Equal(
            McpGatewayMessages.ToolCatalog.RestartAttemptsExhausted,
            harness.Catalog.GetDegradedSources()[McpGatewayConventions.DownstreamSources.Secondary]);

        // No successful restart occurred: generation never bumps and the dispatcher is never told
        // to regenerate the source's catalog entries.
        Assert.Equal(1, harness.Supervisor.ProcessGeneration);
        Assert.Equal(0, harness.Dispatcher.RegenerateCallCount);
        Assert.Equal(1 + maxAttempts, ControlFile.ReadSpawnCount(controlDir));

        Assert.Equal(
            maxAttempts,
            recordedRestarts.Count(m =>
                (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Outcome) == McpGatewayConventions.Telemetry.Outcomes.RestartAttemptFailed));
        Assert.Contains(recordedRestarts, m =>
            (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Source) == McpGatewayConventions.DownstreamSources.Secondary &&
            (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Outcome) == McpGatewayConventions.Telemetry.Outcomes.RestartExhausted);
    }

    [Fact]
    public async Task Dispose_CancelsInProgressRestartLoop_CompletesPromptly()
    {
        string controlDir = ControlFile.CreateDirectory();
        SupervisorHarness harness = CreateHarness(
            controlDir,
            minBackoff: TimeSpan.FromSeconds(3),
            maxBackoff: TimeSpan.FromSeconds(3),
            maxAttempts: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await harness.Supervisor.ListToolsAsync(cts.Token);

            ControlFile.WriteCommand(controlDir, "crash");
            await Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider.System, cts.Token);

            // Triggers the restart loop, which immediately enters a 3s backoff delay.
            await Record.ExceptionAsync(() => harness.Supervisor.ListToolsAsync(cts.Token));
            await Task.Delay(TimeSpan.FromMilliseconds(200), TimeProvider.System, cts.Token);

            var stopwatch = Stopwatch.StartNew();
            await harness.DisposeSupervisorAsync();
            stopwatch.Stop();

            // Shutdown must cancel the in-progress backoff delay rather than waiting out the full
            // 3s (let alone MaxAttempts * MaxBackoff = 15s).
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"DisposeAsync took {stopwatch.Elapsed}, expected the in-progress restart to be cancelled promptly.");
            Assert.Equal(0, harness.Dispatcher.RegenerateCallCount);
            Assert.Empty(harness.Catalog.GetDegradedSources());
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBackoff_StaysWithinConfiguredBounds_AndVariesWithJitter()
    {
        var minBackoff = TimeSpan.FromMilliseconds(250);
        var maxBackoff = TimeSpan.FromSeconds(10);
        DownstreamProcessSupervisor supervisor = CreateBareSupervisor(minBackoff, maxBackoff, maxAttempts: 5);

        Assert.Equal(minBackoff, supervisor.ComputeBackoff(1));

        var highAttemptResults = new HashSet<TimeSpan>();
        for (int i = 0; i < 20; i++)
        {
            TimeSpan backoff = supervisor.ComputeBackoff(10);
            Assert.InRange(backoff, minBackoff, maxBackoff);
            highAttemptResults.Add(backoff);
        }

        // Once the exponential term saturates well past MaxBackoff, jitter still varies the
        // result across calls rather than always returning the same capped value.
        Assert.True(highAttemptResults.Count > 1, "expected jitter to vary the backoff across repeated calls");

        // A very large attempt number must not throw or overflow -- the exponent is capped.
        TimeSpan saturated = supervisor.ComputeBackoff(int.MaxValue);
        Assert.InRange(saturated, minBackoff, maxBackoff);
    }

    private static DownstreamProcessSupervisor CreateBareSupervisor(
        TimeSpan minBackoff,
        TimeSpan maxBackoff,
        int maxAttempts)
    {
        var descriptor = new DownstreamProcessDescriptor(
            "unused",
            "dotnet",
            ["--version"],
            Directory.GetCurrentDirectory(),
            AuthRequired: false,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string?>(StringComparer.Ordinal));
        var client = new DownstreamMcpClient(
            descriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(new DownstreamToolCatalog());
        services.AddSingleton<IGatewayToolDispatcher>(new FakeGatewayToolDispatcher());
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var options = new DownstreamProcessSupervisorOptions(minBackoff, maxBackoff, maxAttempts);

        // Never spawned: ComputeBackoff is pure and touches neither `inner` nor the process.
        return new DownstreamProcessSupervisor(
            client,
            McpGatewayConventions.DownstreamSources.Secondary,
            options,
            serviceProvider,
            TimeProvider.System,
            NullLogger<DownstreamProcessSupervisor>.Instance,
            CancellationToken.None);
    }

    private static SupervisorHarness CreateHarness(
        string controlDir,
        TimeSpan minBackoff,
        TimeSpan maxBackoff,
        int maxAttempts,
        CancellationToken shutdownToken = default)
    {
        string fixtureDllPath = ProcessFixtureLocator.ResolveDllPath();
        var descriptor = new DownstreamProcessDescriptor(
            "process-fixture",
            "dotnet",
            [fixtureDllPath, "--control-dir", controlDir],
            Directory.GetCurrentDirectory(),
            AuthRequired: false,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string?>(StringComparer.Ordinal));

        var client = new DownstreamMcpClient(
            descriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);

        var catalog = new DownstreamToolCatalog();
        var dispatcher = new FakeGatewayToolDispatcher();
        var services = new ServiceCollection();
        services.AddSingleton(catalog);
        services.AddSingleton<IGatewayToolDispatcher>(dispatcher);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        var options = new DownstreamProcessSupervisorOptions(minBackoff, maxBackoff, maxAttempts);
        var supervisor = new DownstreamProcessSupervisor(
            client,
            McpGatewayConventions.DownstreamSources.Secondary,
            options,
            serviceProvider,
            TimeProvider.System,
            NullLogger<DownstreamProcessSupervisor>.Instance,
            shutdownToken);

        return new SupervisorHarness(supervisor, catalog, dispatcher, serviceProvider, controlDir);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Condition not met within {timeout}: {because}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), TimeProvider.System);
        }
    }

    private sealed class SupervisorHarness(
        DownstreamProcessSupervisor supervisor,
        DownstreamToolCatalog catalog,
        FakeGatewayToolDispatcher dispatcher,
        ServiceProvider serviceProvider,
        string controlDir) : IAsyncDisposable
    {
        private bool supervisorDisposed;

        public DownstreamProcessSupervisor Supervisor { get; } = supervisor;

        public DownstreamToolCatalog Catalog { get; } = catalog;

        public FakeGatewayToolDispatcher Dispatcher { get; } = dispatcher;

        /// <summary>
        /// Disposes the supervisor early (e.g. to assert on shutdown timing) without racing the
        /// harness's own <see cref="DisposeAsync"/>, which would otherwise dispose it a second
        /// time and throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        public async ValueTask DisposeSupervisorAsync()
        {
            if (!supervisorDisposed)
            {
                supervisorDisposed = true;
                await Supervisor.DisposeAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeSupervisorAsync();
            await serviceProvider.DisposeAsync();

            if (Directory.Exists(controlDir))
            {
                Directory.Delete(controlDir, recursive: true);
            }
        }
    }

    private sealed class FakeGatewayToolDispatcher : IGatewayToolDispatcher
    {
        private int regenerateCallCount;

        public string? LastRegeneratedSourceId { get; private set; }

        public int RegenerateCallCount => Volatile.Read(ref regenerateCallCount);

        public Task<ListToolsResult> ListToolsAsync(ListToolsRequestParams request, CancellationToken ct) =>
            throw new NotSupportedException("Not used by DownstreamProcessSupervisor tests.");

        public Task<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct) =>
            throw new NotSupportedException("Not used by DownstreamProcessSupervisor tests.");

        public Task RegenerateSourceAsync(string sourceId, CancellationToken ct)
        {
            LastRegeneratedSourceId = sourceId;
            Interlocked.Increment(ref regenerateCallCount);
            return Task.CompletedTask;
        }
    }

    private static object? TagValue(Measurement<long> measurement, string key)
    {
        KeyValuePair<string, object?>[] tags = measurement.Tags.ToArray();
        return tags.First(t => t.Key == key).Value;
    }
}
