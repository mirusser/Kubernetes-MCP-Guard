using System.Text.Json;
using A2A;
using InfraGate.Executor.Handoff;
using InfraGate.Executor.Mcp;
using InfraGate.Executor.Watch;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorAgentHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ValidPlanId_EnqueuesAppliedResult()
    {
        var handler = CreateHandler(out _);

        var events = await ExecuteAndDrainAsync(handler, CreateContext("plan-1"));

        var message = Assert.Single(events).Message!;
        var result = JsonSerializer.Deserialize<ExecutorDispatchResult>(Assert.Single(message.Parts).Text!);
        Assert.Equal(ExecutorDispatchStatuses.Applied, result!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPlanId_ThrowsInvalidParams()
    {
        var handler = CreateHandler(out _);

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            ExecuteAndDrainAsync(handler, CreateContext(string.Empty)));

        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_CapacityExhausted_EnqueuesFailedResult()
    {
        var handler = CreateHandler(out var gate);
        Assert.True(gate.TryAcquire());

        var events = await ExecuteAndDrainAsync(handler, CreateContext("plan-1"));

        var message = Assert.Single(events).Message!;
        var result = JsonSerializer.Deserialize<ExecutorDispatchResult>(Assert.Single(message.Parts).Text!);
        Assert.Equal(ExecutorDispatchStatuses.Failed, result!.Status);
    }

    private static ExecutorAgentHandler CreateHandler(out ExecutorConcurrencyGate gate)
    {
        var options = Options.Create(new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            ConcurrencyCap = 1,
        });
        var optionsMonitor = Substitute.For<IOptionsMonitor<ExecutorOptions>>();
        optionsMonitor.CurrentValue.Returns(options.Value);
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Approved","timedOut":false}""");
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Applied"}""");
        gate = new ExecutorConcurrencyGate(options);
        return new ExecutorAgentHandler(
            gate,
            new PlanWatcher(
                new ExecutorDedupeStore(),
                mcpClient,
                optionsMonitor,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PlanWatcher>.Instance));
    }

    private static RequestContext CreateContext(string planId) =>
        new()
        {
            TaskId = "task-1",
            ContextId = "anomaly-1",
            StreamingResponse = false,
            Message = new Message
            {
                MessageId = "message-1",
                Role = Role.User,
                Parts = [new Part { Text = planId }],
            },
        };

    private static async Task<List<StreamResponse>> ExecuteAndDrainAsync(
        ExecutorAgentHandler handler,
        RequestContext context)
    {
        var events = new List<StreamResponse>();
        var eventQueue = new AgentEventQueue();
        var readerTask = DrainAsync(eventQueue, events);
        try
        {
            await handler.ExecuteAsync(context, eventQueue, CancellationToken.None);
        }
        finally
        {
            eventQueue.Complete(null);
            await readerTask;
        }
        return events;
    }

    private static async Task DrainAsync(AgentEventQueue queue, List<StreamResponse> events)
    {
        await foreach (var e in queue.WithCancellation(CancellationToken.None))
            events.Add(e);
    }
}
