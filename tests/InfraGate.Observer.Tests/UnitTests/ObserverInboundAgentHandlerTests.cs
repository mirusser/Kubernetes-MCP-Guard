using A2A;
using InfraGate.AgentGuardrails;
using InfraGate.Observer.Audit;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverInboundAgentHandlerTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static ObserverInboundAgentHandler CreateHandler(
        CapturingAuditOutbox? auditOutbox = null,
        IAgentMcpToolset? mcpToolset = null,
        AgentGuardrailPolicy? guardrailPolicy = null) =>
        new(NullLogger<ObserverInboundAgentHandler>.Instance, auditOutbox, mcpToolset, guardrailPolicy);

    private static RequestContext ContextWithEnvelope(ObserverInboundEnvelope envelope)
    {
        string json = JsonSerializer.Serialize(envelope);
        return new RequestContext
        {
            TaskId = "task-1",
            ContextId = "ctx-1",
            StreamingResponse = false,
            Message = new Message
            {
                MessageId = "msg-1",
                Role = A2A.Role.User,
                Parts = [new Part { Text = json }],
            },
        };
    }

    private static async Task<List<StreamResponse>> ExecuteAndDrainAsync(
        ObserverInboundAgentHandler handler,
        RequestContext context)
    {
        var events = new List<StreamResponse>();
        var eventQueue = new AgentEventQueue();
        var readerTask = DrainAsync(eventQueue, events);
        await handler.ExecuteAsync(context, eventQueue, CancellationToken.None);
        eventQueue.Complete(null);
        await readerTask;
        return events;
    }

    private static async Task DrainAsync(AgentEventQueue queue, List<StreamResponse> events)
    {
        await foreach (var e in queue.WithCancellation(CancellationToken.None))
            events.Add(e);
    }

    // ── Progress intent ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ProgressIntent_WritesHandoffProgressAuditEntry()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var handler = CreateHandler(auditOutbox);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = "cycle-1",
            Progress = new PlanProgressPayload { Stage = PlanProgressStage.Analyzing },
        });

        await ExecuteAndDrainAsync(handler, context);

        Assert.Single(auditOutbox.Entries);
        var entry = auditOutbox.Entries[0];
        Assert.Equal(ObserverAuditEvents.HandoffProgress, entry.EventName);
        Assert.Equal("cycle-1", entry.CycleId);
        Assert.Equal("service:planner", entry.ActorSubject);
        Assert.Equal("received", entry.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ProgressIntent_RespondsWithAck()
    {
        var handler = CreateHandler();
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = "cycle-1",
            Progress = new PlanProgressPayload { Stage = PlanProgressStage.PlanProposed, ProposalCount = 2 },
        });

        var events = await ExecuteAndDrainAsync(handler, context);

        Assert.Single(events);
        Assert.Equal(StreamResponseCase.Message, events[0].PayloadCase);
        var text = events[0].Message?.Parts.FirstOrDefault()?.Text;
        Assert.Equal("ack", text);
    }

    [Fact]
    public async Task ExecuteAsync_ProgressIntent_NullAuditOutbox_DoesNotThrow()
    {
        var handler = CreateHandler(auditOutbox: null);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = "cycle-1",
            Progress = new PlanProgressPayload { Stage = PlanProgressStage.Failed },
        });

        var ex = await Record.ExceptionAsync(() => ExecuteAndDrainAsync(handler, context));

        Assert.Null(ex);
    }

    // ── Unknown intent ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnknownIntent_RespondsWithError()
    {
        var handler = CreateHandler();
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = "something-unknown",
            CycleId = "cycle-1",
        });

        var events = await ExecuteAndDrainAsync(handler, context);

        Assert.Single(events);
        var text = events[0].Message?.Parts.FirstOrDefault()?.Text;
        Assert.NotNull(text);
        Assert.StartsWith("error:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownIntent_DoesNotWriteAuditEntry()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var handler = CreateHandler(auditOutbox);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = "something-unknown",
            CycleId = "cycle-1",
        });

        await ExecuteAndDrainAsync(handler, context);

        Assert.Empty(auditOutbox.Entries);
    }

    // ── Malformed request ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullMessageText_RespondsWithError()
    {
        var handler = CreateHandler();
        var context = new RequestContext
        {
            TaskId = "t",
            ContextId = "c",
            StreamingResponse = false,
            Message = new Message { MessageId = "m", Role = A2A.Role.User, Parts = [] },
        };

        var events = await ExecuteAndDrainAsync(handler, context);

        Assert.Single(events);
        var text = events[0].Message?.Parts.FirstOrDefault()?.Text;
        Assert.NotNull(text);
        Assert.StartsWith("error:", text, StringComparison.Ordinal);
    }

    // ── Tool-request intent ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ToolRequestIntent_AllowedTool_ReturnsSerializedResult()
    {
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_events" });
        var toolset = new CapturingMcpToolset("pod events output");
        var handler = CreateHandler(mcpToolset: toolset, guardrailPolicy: policy);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-1",
            ToolRequest = new ToolRequestPayload { ToolName = "get_k8s_events" },
        });

        var events = await ExecuteAndDrainAsync(handler, context);

        Assert.Single(events);
        var text = events[0].Message?.Parts.FirstOrDefault()?.Text;
        Assert.NotNull(text);
        var payload = JsonSerializer.Deserialize<ToolResponsePayload>(text);
        Assert.NotNull(payload);
        Assert.False(payload.IsError);
        Assert.Equal("pod events output", payload.ResultJson);
    }

    [Fact]
    public async Task ExecuteAsync_ToolRequestIntent_AllowedTool_WritesToolServedAudit()
    {
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_events" });
        var toolset = new CapturingMcpToolset("result");
        var auditOutbox = new CapturingAuditOutbox();
        var handler = CreateHandler(auditOutbox: auditOutbox, mcpToolset: toolset, guardrailPolicy: policy);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-1",
            ToolRequest = new ToolRequestPayload { ToolName = "get_k8s_events" },
        });

        await ExecuteAndDrainAsync(handler, context);

        Assert.Single(auditOutbox.Entries);
        Assert.Equal(ObserverAuditEvents.HandoffToolServed, auditOutbox.Entries[0].EventName);
        Assert.Equal("cycle-1", auditOutbox.Entries[0].CycleId);
    }

    [Fact]
    public async Task ExecuteAsync_ToolRequestIntent_DeniedTool_ReturnsIsErrorPayload()
    {
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_events" });
        var handler = CreateHandler(guardrailPolicy: policy);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-1",
            ToolRequest = new ToolRequestPayload { ToolName = "delete_pod" },
        });

        var events = await ExecuteAndDrainAsync(handler, context);

        Assert.Single(events);
        var text = events[0].Message?.Parts.FirstOrDefault()?.Text;
        Assert.NotNull(text);
        var payload = JsonSerializer.Deserialize<ToolResponsePayload>(text);
        Assert.NotNull(payload);
        Assert.True(payload.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_ToolRequestIntent_DeniedTool_WritesToolDeniedAudit()
    {
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_events" });
        var auditOutbox = new CapturingAuditOutbox();
        var handler = CreateHandler(auditOutbox: auditOutbox, guardrailPolicy: policy);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-1",
            ToolRequest = new ToolRequestPayload { ToolName = "delete_pod" },
        });

        await ExecuteAndDrainAsync(handler, context);

        Assert.Single(auditOutbox.Entries);
        Assert.Equal(ObserverAuditEvents.HandoffToolDenied, auditOutbox.Entries[0].EventName);
        Assert.Equal("cycle-1", auditOutbox.Entries[0].CycleId);
    }

    [Fact]
    public async Task ExecuteAsync_ToolRequestIntent_NullGuardrailPolicy_DeniesAll()
    {
        var toolset = new CapturingMcpToolset("result");
        var handler = CreateHandler(mcpToolset: toolset, guardrailPolicy: null);
        var context = ContextWithEnvelope(new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-1",
            ToolRequest = new ToolRequestPayload { ToolName = "get_k8s_events" },
        });

        var events = await ExecuteAndDrainAsync(handler, context);

        var payload = JsonSerializer.Deserialize<ToolResponsePayload>(
            events[0].Message?.Parts.FirstOrDefault()?.Text ?? "{}");
        Assert.True(payload?.IsError);
        Assert.Equal(0, toolset.CallCount);
    }

    // ── CancelAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_IsNoOp()
    {
        var handler = CreateHandler();
        var context = new RequestContext
        {
            TaskId = "t",
            ContextId = "c",
            StreamingResponse = false,
            Message = new Message { MessageId = "m", Role = A2A.Role.User, Parts = [] },
        };
        var eventQueue = new AgentEventQueue();

        var ex = await Record.ExceptionAsync(() =>
            handler.CancelAsync(context, eventQueue, CancellationToken.None));

        Assert.Null(ex);
    }

    // ── Fake MCP toolset ─────────────────────────────────────────────

    private sealed class CapturingMcpToolset(string resultText) : IAgentMcpToolset
    {
        public int CallCount { get; private set; }
        public string GatewayBaseUrl => "http://stub";
        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AITool>>([]);

        public Task<CallToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = resultText }],
                IsError = false,
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── Fake audit outbox ─────────────────────────────────────────────

    private sealed class CapturingAuditOutbox : IObserverAuditOutbox
    {
        public List<ObserverAuditEntry> Entries { get; } = [];

        public Task<long> AppendAsync(ObserverAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }

        public Task<long> AppendAsync(
            ObserverAuditEntry entry,
            Npgsql.NpgsqlConnection connection,
            Npgsql.NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }
    }
}
