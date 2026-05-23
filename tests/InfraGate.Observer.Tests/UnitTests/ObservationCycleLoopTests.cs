using InfraGate.Observer.Cycle;
using InfraGate.Observer.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObservationCycleLoopTests
{
    private static ObservationCycleLoop CreateLoop(int cadenceSeconds = 60)
    {
        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns(new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            CycleIntervalSeconds = cadenceSeconds,
        });

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CycleResult
            {
                CycleId = Guid.NewGuid().ToString("D"),
                Reports = Array.Empty<AnomalyReport>(),
                IsTruncated = false,
                ToolCallsUsed = 0,
                SeverityDisagreements = 0,
                Duration = TimeSpan.Zero,
            }));

        return new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
    }

    [Fact]
    public async Task StartAsync_ValidOptions_CompletesWithoutException()
    {
        var loop = CreateLoop();
        await loop.StartAsync(CancellationToken.None);
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenCalledTwice_DisposesPreviousTimer()
    {
        var loop = CreateLoop();
        await loop.StartAsync(CancellationToken.None);
        await loop.StartAsync(CancellationToken.None);
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_CompletesWithoutException()
    {
        var loop = CreateLoop();
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Dispose_DisposesResources()
    {
        var loop = CreateLoop();
        loop.Dispose();
    }

    [Fact]
    public async Task Dispose_AfterStartAndStop_DoesNotThrow()
    {
        var loop = CreateLoop();
        await loop.StartAsync(CancellationToken.None);
        await loop.StopAsync(CancellationToken.None);
        loop.Dispose();
    }

    [Fact]
    public async Task StartAsync_OptionsValueNull_ThrowsNullReferenceException()
    {
        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns((ObserverOptions)null!);

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        var loop = new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
        await Assert.ThrowsAsync<NullReferenceException>(() => loop.StartAsync(CancellationToken.None));
    }

    // ── ExecuteCycle behavioral tests ───────────────────────────────────
#pragma warning disable MA0167 // Task.Delay without TimeProvider is acceptable in tests

    [Fact]
    public async Task ExecuteCycle_WhenRunnerCompletes_RunAsyncIsCalledOnce()
    {
        var tcs = new TaskCompletionSource<bool>();
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                tcs.SetResult(true);
                return Task.FromResult(new CycleResult
                {
                    CycleId = "test",
                    Reports = Array.Empty<AnomalyReport>(),
                    IsTruncated = false,
                    ToolCallsUsed = 0,
                    SeverityDisagreements = 0,
                    Duration = TimeSpan.Zero,
                });
            });

        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns(new ObserverOptions { GatewayBaseUrl = "http://localhost:3001/mcp", CycleIntervalSeconds = 60 });

        var loop = new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
        await loop.StartAsync(CancellationToken.None);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        await cycleRunner.Received(1).RunAsync(Arg.Any<CancellationToken>());
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteCycle_WhenRunnerThrows_ResetsGuard()
    {
        var tcs = new TaskCompletionSource<bool>();
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns<Task<CycleResult>>(_ =>
            {
                tcs.TrySetResult(true);
                return Task.FromException<CycleResult>(new InvalidOperationException("test failure"));
            });

        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns(new ObserverOptions { GatewayBaseUrl = "http://localhost:3001/mcp", CycleIntervalSeconds = 60 });

        var loop = new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
        await loop.StartAsync(CancellationToken.None);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        await cycleRunner.Received(1).RunAsync(Arg.Any<CancellationToken>());
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteCycle_WhenPreviousCycleInFlight_SkipsExecution()
    {
        var firstCycleRunning = new TaskCompletionSource<bool>();
        var firstCycleCanComplete = new TaskCompletionSource<bool>();

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                firstCycleRunning.TrySetResult(true);
                await firstCycleCanComplete.Task;
                return new CycleResult
                {
                    CycleId = "test",
                    Reports = Array.Empty<AnomalyReport>(),
                    IsTruncated = false,
                    ToolCallsUsed = 0,
                    SeverityDisagreements = 0,
                    Duration = TimeSpan.Zero,
                };
            });

        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns(new ObserverOptions { GatewayBaseUrl = "http://localhost:3001/mcp", CycleIntervalSeconds = 1 });

        var loop = new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
        await loop.StartAsync(CancellationToken.None);

        await firstCycleRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(1500);

        firstCycleCanComplete.SetResult(true);
        await Task.Delay(200);

        await cycleRunner.Received(1).RunAsync(Arg.Any<CancellationToken>());
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteCycle_WhenIsTruncated_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<bool>();
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                tcs.SetResult(true);
                return Task.FromResult(new CycleResult
                {
                    CycleId = "test",
                    Reports = Array.Empty<AnomalyReport>(),
                    IsTruncated = true,
                    ToolCallsUsed = 1,
                    SeverityDisagreements = 0,
                    Duration = TimeSpan.FromMilliseconds(500),
                });
            });

        var options = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        options.CurrentValue.Returns(new ObserverOptions { GatewayBaseUrl = "http://localhost:3001/mcp", CycleIntervalSeconds = 60 });

        var loop = new ObservationCycleLoop(options, cycleRunner, NullLogger<ObservationCycleLoop>.Instance);
        await loop.StartAsync(CancellationToken.None);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        await cycleRunner.Received(1).RunAsync(Arg.Any<CancellationToken>());
        await loop.StopAsync(CancellationToken.None);
    }
}
