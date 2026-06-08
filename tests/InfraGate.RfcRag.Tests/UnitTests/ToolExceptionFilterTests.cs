using System.Runtime.CompilerServices;
using InfraGate.RfcRag.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class ToolExceptionFilterTests
{
    private static CallToolRequestParams TestParams(string name) => new() { Name = name };

    private static RequestContext<CallToolRequestParams> TestRequest(string toolName)
    {
        var request = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        request.Params = TestParams(toolName);
        return request;
    }

    private static CallToolResult TestResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    [Fact]
    public async Task CreateSafetyNet_SuccessfulToolCall_PassesThroughResult()
    {
        var expected = TestResult("success");
        var handler = ToolExceptionFilter.CreateSafetyNet(
            (_, _) => new ValueTask<CallToolResult>(expected),
            NullLogger.Instance);

        var result = await handler(TestRequest("test_tool"), CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task CreateSafetyNet_TimeoutExceptionNotFromCancellation_ReturnsIsErrorTrue()
    {
        var handler = ToolExceptionFilter.CreateSafetyNet(
            (_, _) => throw new OperationCanceledException("The request timed out"),
            NullLogger.Instance);

        var result = await handler(TestRequest("test_tool"), CancellationToken.None);

        Assert.True(result.IsError);
        var content = Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(content);
        Assert.StartsWith("Tool 'test_tool' timed out", textBlock.Text);
    }

    [Fact]
    public async Task CreateSafetyNet_UnhandledException_ReturnsIsErrorTrue()
    {
        var handler = ToolExceptionFilter.CreateSafetyNet(
            (_, _) => throw new InvalidOperationException("Something went wrong"),
            NullLogger.Instance);

        var result = await handler(TestRequest("test_tool"), CancellationToken.None);

        Assert.True(result.IsError);
        var content = Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(content);
        Assert.StartsWith("Tool 'test_tool' failed", textBlock.Text);
        Assert.Contains("Something went wrong", textBlock.Text);
    }

    [Fact]
    public async Task CreateSafetyNet_GenuineCancellation_PropagatesException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = ToolExceptionFilter.CreateSafetyNet(
            (_, _) => throw new OperationCanceledException(),
            NullLogger.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler(TestRequest("test_tool"), cts.Token).AsTask());
    }
}
