using InfraGate.Executor;
using InfraGate.Executor.Queue;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ProposalQueueTests
{
    private static ProposalQueue CreateQueue(int cap = 2) =>
        new(Options.Create(new ExecutorOptions { GatewayBaseUrl = "http://localhost", ConcurrencyCap = cap }));

    private static RemediationProposal MakeProposal(string planId = "plan-1") =>
        new() { PlanId = planId, AnomalyId = "anomaly-1", ProposedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void TryEnqueueAll_EmptyList_ReturnsTrue()
    {
        using var queue = CreateQueue();
        bool result = queue.TryEnqueueAll([]);
        Assert.True(result);
    }

    [Fact]
    public void TryEnqueueAll_WithinCap_ReturnsTrueAndItemsReachChannel()
    {
        using var queue = CreateQueue(cap: 2);
        var proposals = new[]
        {
            MakeProposal("plan-1"),
            MakeProposal("plan-2"),
        };

        bool result = queue.TryEnqueueAll(proposals);

        Assert.True(result);
        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("plan-1", first!.PlanId);
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.Equal("plan-2", second!.PlanId);
    }

    [Fact]
    public void TryEnqueueAll_ExceedsCap_ReturnsFalseAndNoItemsQueued()
    {
        using var queue = CreateQueue(cap: 1);
        var proposals = new[]
        {
            MakeProposal("plan-1"),
            MakeProposal("plan-2"),
        };

        bool result = queue.TryEnqueueAll(proposals);

        Assert.False(result);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void TryEnqueueAll_AtExactCap_ReturnsTrue()
    {
        using var queue = CreateQueue(cap: 3);
        var proposals = new[]
        {
            MakeProposal("plan-1"),
            MakeProposal("plan-2"),
            MakeProposal("plan-3"),
        };

        bool result = queue.TryEnqueueAll(proposals);

        Assert.True(result);
        Assert.Equal(0, queue.AvailableSlots);
    }

    [Fact]
    public void ReleaseSlot_AfterEnqueue_IncreasesAvailableSlots()
    {
        using var queue = CreateQueue(cap: 2);
        queue.TryEnqueueAll([MakeProposal()]);
        int before = queue.AvailableSlots;

        queue.ReleaseSlot();

        Assert.Equal(before + 1, queue.AvailableSlots);
    }

    [Fact]
    public void TryEnqueueAll_AfterCapExhaustedThenReleased_AcceptsNewProposal()
    {
        using var queue = CreateQueue(cap: 1);
        queue.TryEnqueueAll([MakeProposal("plan-1")]);
        Assert.Equal(0, queue.AvailableSlots);

        queue.ReleaseSlot();

        bool result = queue.TryEnqueueAll([MakeProposal("plan-2")]);
        Assert.True(result);
    }
}
