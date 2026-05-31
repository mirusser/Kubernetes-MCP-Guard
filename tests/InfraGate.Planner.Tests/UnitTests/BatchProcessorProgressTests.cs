using InfraGate.AgentLlm;
using InfraGate.AgentMcp;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Handoff;
using InfraGate.Prompts;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class BatchProcessorProgressTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static BatchProcessor CreateProcessor(
        AnomalyBatchQueue? queue = null,
        IObserverChannel? channel = null,
        IAgentMcpToolset? mcp = null) =>
        new(
            new FakeOptionsMonitor<PlannerOptions>(new PlannerOptions
            {
                GatewayBaseUrl = "http://gateway",
                LlmApiKey = "test",
            }),
            queue ?? new AnomalyBatchQueue(),
            new ToolCallingAgentFactory(new FixtureChatClient("no-action")),
            mcp ?? new StubMcpToolset(),
            new NullRemediationProposalSink(),
            NullLogger<BatchProcessor>.Instance,
            new StubPromptLibrary(),
            observerChannel: channel);

    private static AnomalyHandoffBatch EmptyBatch(string cycleId = "cycle-1") => new()
    {
        CycleId = cycleId,
        EmittedAt = DateTimeOffset.UtcNow,
        Reports = [],
    };

    // ── Progress milestones — direct ProcessBatchAsync ────────────────

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_SendsAnalyzingThenNoAction()
    {
        var channel = new CapturingObserverChannel();
        var processor = CreateProcessor(channel: channel);

        await processor.ProcessBatchAsync(EmptyBatch("cycle-1"), CancellationToken.None);

        Assert.Equal(2, channel.Calls.Count);
        Assert.Equal(("cycle-1", PlanProgressStage.Analyzing), (channel.Calls[0].CycleId, channel.Calls[0].Stage));
        Assert.Equal(("cycle-1", PlanProgressStage.NoAction), (channel.Calls[1].CycleId, channel.Calls[1].Stage));
    }

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_AnalyzingStageCarriesCycleId()
    {
        var channel = new CapturingObserverChannel();
        var processor = CreateProcessor(channel: channel);

        await processor.ProcessBatchAsync(EmptyBatch("my-cycle"), CancellationToken.None);

        Assert.All(channel.Calls, c => Assert.Equal("my-cycle", c.CycleId));
    }

    [Fact]
    public async Task ProcessBatchAsync_NullChannel_CompletesWithoutThrowing()
    {
        var processor = CreateProcessor(channel: null);

        var ex = await Record.ExceptionAsync(() =>
            processor.ProcessBatchAsync(EmptyBatch(), CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessBatchAsync_ChannelThrows_DoesNotAbortProcessing()
    {
        // ThrowingObserverChannel throws on every call.
        // The resilience against a failing IObserverChannel lives in BatchProcessor.SendProgressSafeAsync,
        // which catches all exceptions (except OCE when the host is shutting down).
        // A well-behaved channel (ObserverChannel) adds its own internal swallow as a second layer,
        // but BatchProcessor itself must never propagate a progress-send failure.
        var channel = new ThrowingObserverChannel();
        var processor = CreateProcessor(channel: channel);

        var ex = await Record.ExceptionAsync(() =>
            processor.ProcessBatchAsync(EmptyBatch(), CancellationToken.None));

        // SendProgressSafeAsync caught the throw from Analyzing → ProcessBatchAsync completes.
        Assert.Null(ex);
    }

    // ── Failed progress — via ExecuteAsync ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ToolsFetchFails_SendsAnalyzingAndFailed()
    {
        var channel = new SignalingObserverChannel();
        var batchQueue = new AnomalyBatchQueue();
        batchQueue.TryEnqueue(EmptyBatch("cycle-fail"));

        var processor = CreateProcessor(queue: batchQueue, channel: channel, mcp: new FailingMcpToolset());
        await processor.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool signaled;
        try
        {
            await channel.WhenFailed.WaitAsync(cts.Token);
            signaled = true;
        }
        catch (OperationCanceledException)
        {
            signaled = false;
        }

        await processor.StopAsync(CancellationToken.None);

        Assert.True(signaled, "Expected Failed stage to be sent within 5 seconds");
        Assert.Equal(PlanProgressStage.Analyzing, channel.Stages.FirstOrDefault());
        Assert.Equal(PlanProgressStage.Failed, channel.Stages.LastOrDefault());
    }

    // ── Fakes ──────────────────────────────────────────────────────────

    private sealed class CapturingObserverChannel : IObserverChannel
    {
        public List<(string CycleId, string Stage)> Calls { get; } = [];

        public Task SendProgressAsync(
            string cycleId, string stage, string? detail, int? proposalCount, CancellationToken cancellationToken = default)
        {
            Calls.Add((cycleId, stage));
            return Task.CompletedTask;
        }

        public Task<ToolResponsePayload> SendToolRequestAsync(
            string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResponsePayload { IsError = false, ResultJson = string.Empty });
    }

    private sealed class SignalingObserverChannel : IObserverChannel
    {
        private readonly TaskCompletionSource<bool> failedSignal = new();
        public Task<bool> WhenFailed => failedSignal.Task;
        public List<string> Stages { get; } = [];

        public Task SendProgressAsync(
            string cycleId, string stage, string? detail, int? proposalCount, CancellationToken cancellationToken = default)
        {
            Stages.Add(stage);
            if (string.Equals(stage, PlanProgressStage.Failed, StringComparison.Ordinal))
                failedSignal.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task<ToolResponsePayload> SendToolRequestAsync(
            string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResponsePayload { IsError = false, ResultJson = string.Empty });
    }

    private sealed class ThrowingObserverChannel : IObserverChannel
    {
        public Task SendProgressAsync(
            string cycleId, string stage, string? detail, int? proposalCount, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated channel failure");

        public Task<ToolResponsePayload> SendToolRequestAsync(
            string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated channel failure");
    }

    private sealed class StubMcpToolset : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://stub";
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
        public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(new CallToolResult { IsError = true });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingMcpToolset : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://stub";
        public bool IsConnected => false;
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("MCP unavailable");
        public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(new CallToolResult { IsError = true });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullRemediationProposalSink : IRemediationProposalSink
    {
        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubPromptLibrary : IPromptLibrary
    {
        public Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
