using System.Text.Json;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Llm;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class AskObserverToolTests
{
    // ── Name and description ──────────────────────────────────────────

    [Fact]
    public void Create_ReturnsFunctionWithExpectedName()
    {
        var function = AskObserverTool.Create(new StubObserverChannel(), "cycle-1");

        Assert.Equal(AskObserverTool.FunctionName, function.Name);
    }

    [Fact]
    public void Create_ReturnsFunctionWithDescription()
    {
        var function = AskObserverTool.Create(new StubObserverChannel(), "cycle-1");

        Assert.False(string.IsNullOrWhiteSpace(function.Description));
    }

    // ── Delegate behavior ─────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_DelegatesToObserverChannel()
    {
        var channel = new CapturingObserverChannel();
        var function = AskObserverTool.Create(channel, "cycle-42");

        await InvokeAsync(function, "get_k8s_events", null);

        Assert.Single(channel.Requests);
        Assert.Equal("cycle-42", channel.Requests[0].CycleId);
        Assert.Equal("get_k8s_events", channel.Requests[0].ToolName);
        Assert.Null(channel.Requests[0].ArgumentsJson);
    }

    [Fact]
    public async Task InvokeAsync_PassesArgumentsJson()
    {
        var channel = new CapturingObserverChannel();
        var function = AskObserverTool.Create(channel, "cycle-1");

        await InvokeAsync(function, "get_k8s_pods", "{\"namespace\":\"default\"}");

        Assert.Equal("{\"namespace\":\"default\"}", channel.Requests[0].ArgumentsJson);
    }

    [Fact]
    public async Task InvokeAsync_SuccessResult_ChannelIsCalledOnce()
    {
        var channel = new CapturingObserverChannel(respondWith: new ToolResponsePayload
        {
            IsError = false,
            ResultJson = "pod-1 Running",
        });
        var function = AskObserverTool.Create(channel, "cycle-1");

        await InvokeAsync(function, "get_k8s_pods", null);

        Assert.Single(channel.Requests);
        Assert.Equal("get_k8s_pods", channel.Requests[0].ToolName);
    }

    [Fact]
    public async Task InvokeAsync_IsErrorResult_DoesNotThrow()
    {
        var channel = new CapturingObserverChannel(respondWith: new ToolResponsePayload
        {
            IsError = true,
            ResultJson = "tool_denied",
        });
        var function = AskObserverTool.Create(channel, "cycle-1");

        var ex = await Record.ExceptionAsync(() => InvokeAsync(function, "delete_pod", null));

        Assert.Null(ex);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static async Task<string> InvokeAsync(
        AIFunction function,
        string toolName,
        string? argumentsJson)
    {
        var args = new AIFunctionArguments
        {
            ["toolName"] = toolName,
            ["argumentsJson"] = argumentsJson,
        };
        var result = await function.InvokeAsync(args, CancellationToken.None);
        // AIFunctionFactory serializes the return value to JsonElement.
        if (result is string s) return s;
        if (result is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? (je.GetString() ?? string.Empty) : je.GetRawText();
        return string.Empty;
    }

    // ── Fakes ─────────────────────────────────────────────────────────

    private sealed class StubObserverChannel : IObserverChannel
    {
        public Task SendProgressAsync(
            string cycleId, string stage, string? detail, int? proposalCount, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ToolResponsePayload> SendToolRequestAsync(
            string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResponsePayload { IsError = false, ResultJson = string.Empty });
    }

    private sealed class CapturingObserverChannel(ToolResponsePayload? respondWith = null) : IObserverChannel
    {
        public List<(string CycleId, string ToolName, string? ArgumentsJson)> Requests { get; } = [];

        public Task SendProgressAsync(
            string cycleId, string stage, string? detail, int? proposalCount, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ToolResponsePayload> SendToolRequestAsync(
            string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
        {
            Requests.Add((cycleId, toolName, argumentsJson));
            return Task.FromResult(respondWith ?? new ToolResponsePayload { IsError = false, ResultJson = string.Empty });
        }
    }
}
