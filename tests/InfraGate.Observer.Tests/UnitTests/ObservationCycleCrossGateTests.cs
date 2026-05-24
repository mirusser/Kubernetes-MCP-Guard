#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
#pragma warning disable CA2008
using InfraGate.Observer.Cycle;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObservationCycleCrossGateTests
{
    [Fact]
    public async Task Semaphore_SerialisesBetweenAcquireModes()
    {
        var serialisation = new CycleSerialisation();

        var scheduledHeld = new TaskCompletionSource<bool>();
        var scheduledCanRelease = new TaskCompletionSource<bool>();

        var scheduledTask = Task.Run(async () =>
        {
            await serialisation.TryAcquireScheduledAsync(CancellationToken.None);
            scheduledHeld.TrySetResult(true);
            await scheduledCanRelease.Task;
            serialisation.Release();
        });

        await scheduledHeld.Task;

        var onDemandStarted = new TaskCompletionSource<bool>();
        var onDemandWaitOver = new TaskCompletionSource<bool>();
        var onDemandTask = Task.Run(async () =>
        {
            onDemandStarted.TrySetResult(true);
            await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
            onDemandWaitOver.TrySetResult(true);
            serialisation.Release();
        });

        await onDemandStarted.Task;
        await Task.Delay(200);
        Assert.False(onDemandWaitOver.Task.IsCompleted);

        scheduledCanRelease.TrySetResult(true);
        await onDemandWaitOver.Task;

        Assert.True(onDemandWaitOver.Task.IsCompleted);
    }

    [Fact]
    public async Task TryAcquireScheduledAsync_FailsWhenOnDemandHolds()
    {
        var serialisation = new CycleSerialisation();

        var onDemandHeld = new TaskCompletionSource<bool>();
        var onDemandCanRelease = new TaskCompletionSource<bool>();
        var onDemandTask = Task.Run(async () =>
        {
            await serialisation.AcquireForOnDemandAsync(CancellationToken.None);
            onDemandHeld.TrySetResult(true);
            await onDemandCanRelease.Task;
            serialisation.Release();
        });

        await onDemandHeld.Task;

        bool acquired = await serialisation.TryAcquireScheduledAsync(CancellationToken.None);
        Assert.False(acquired);

        onDemandCanRelease.TrySetResult(true);
        await onDemandTask;
    }
}
