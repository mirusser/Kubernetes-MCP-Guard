using InfraGate.Observer.Cycle;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class CycleSerialisationTests
{
    [Fact]
    public async Task TryAcquireScheduledAsync_WhenNotHeld_ReturnsTrue()
    {
        var serialisation = new CycleSerialisation();

        bool acquired = await serialisation.TryAcquireScheduledAsync(CancellationToken.None);

        Assert.True(acquired);
    }

    [Fact]
    public async Task TryAcquireScheduledAsync_WhenHeld_ReturnsFalse()
    {
        var serialisation = new CycleSerialisation();
        await serialisation.TryAcquireScheduledAsync(CancellationToken.None);

        bool acquired = await serialisation.TryAcquireScheduledAsync(CancellationToken.None);

        Assert.False(acquired);
    }

    [Fact]
    public async Task AcquireForOnDemandAsync_WhenNotHeld_AcquiresImmediately()
    {
        var serialisation = new CycleSerialisation();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500));
    }

#pragma warning disable MA0167 // Task.Delay without TimeProvider is acceptable in tests

    [Fact]
    public async Task AcquireForOnDemandAsync_WhenScheduledCycleIsHeld_WaitsForRelease()
    {
        var serialisation = new CycleSerialisation();
        var held = new TaskCompletionSource<bool>();

        var scheduledTask = Task.Run(async () =>
        {
            await serialisation.TryAcquireScheduledAsync(CancellationToken.None);
            held.SetResult(true);
            await Task.Delay(200);
            serialisation.Release();
        });

        await held.Task;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed > TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task Release_AfterAcquire_AllowsNextAcquire()
    {
        var serialisation = new CycleSerialisation();
        await serialisation.TryAcquireScheduledAsync(CancellationToken.None);
        serialisation.Release();

        bool acquired = await serialisation.TryAcquireScheduledAsync(CancellationToken.None);

        Assert.True(acquired);
    }

    [Fact]
    public async Task AcquireForOnDemandAsync_WhenCalledConcurrently_SerialisesAccess()
    {
        var serialisation = new CycleSerialisation();
        var firstEntered = new TaskCompletionSource<bool>();
        var firstCanExit = new TaskCompletionSource<bool>();
        var secondCompleted = new TaskCompletionSource<bool>();

        var firstTask = Task.Run(async () =>
        {
            await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
            firstEntered.SetResult(true);
            await firstCanExit.Task;
            serialisation.Release();
        });

        await firstEntered.Task;

        var secondTask = Task.Run(async () =>
        {
            await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
            secondCompleted.SetResult(true);
            serialisation.Release();
        });

        // Second should NOT have completed yet
        await Task.Delay(200);
        Assert.False(secondCompleted.Task.IsCompleted);

        firstCanExit.SetResult(true);
        await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(secondCompleted.Task.IsCompleted);
    }

#pragma warning restore MA0167
}
