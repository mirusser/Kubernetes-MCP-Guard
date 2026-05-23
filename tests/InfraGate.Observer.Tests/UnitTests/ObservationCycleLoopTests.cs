using InfraGate.Observer.Cycle;
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

        return new ObservationCycleLoop(options, NullLogger<ObservationCycleLoop>.Instance);
    }

    [Fact]
    public async Task StartAsync_ValidOptions_CompletesWithoutException()
    {
        var loop = CreateLoop();
        await loop.StartAsync(CancellationToken.None);
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotThrow()
    {
        var loop = CreateLoop();
        await loop.StartAsync(CancellationToken.None);
        await loop.StartAsync(CancellationToken.None);
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

        var loop = new ObservationCycleLoop(options, NullLogger<ObservationCycleLoop>.Instance);
        await Assert.ThrowsAsync<NullReferenceException>(() => loop.StartAsync(CancellationToken.None));
    }
}
