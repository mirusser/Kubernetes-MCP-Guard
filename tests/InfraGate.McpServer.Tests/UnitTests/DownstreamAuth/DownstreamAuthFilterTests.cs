using System.Runtime.CompilerServices;
using InfraGate.DownstreamAuth;
using InfraGate.McpServer.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Tests for DownstreamAuthFilter focusing on the Required=false pass-through path.
/// </summary>
public sealed class DownstreamAuthFilterTests
{
    private static RequestContext<CallToolRequestParams> MakeCallToolRequest(IServiceProvider? services = null)
    {
        var request = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        request.Params = new CallToolRequestParams { Name = "test_tool" };
        request.Services = services;
        return request;
    }

    private static RequestContext<ListToolsRequestParams> MakeListToolsRequest(IServiceProvider? services = null)
    {
        var request = (RequestContext<ListToolsRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<ListToolsRequestParams>));
        request.Params = new ListToolsRequestParams();
        request.Services = services;
        return request;
    }

    /// <summary>
    /// When no DownstreamTokenValidator is registered in DI (Required=false scenario),
    /// the filter must pass the request through without throwing.
    /// This is the regression test for the GetRequiredService → GetService fix.
    /// </summary>
    [Fact]
    public async Task CallTool_NoValidatorInDi_PassesThroughWithoutThrowing()
    {
        // Arrange: DI container with NO DownstreamTokenValidator registered
        var services = new ServiceCollection().BuildServiceProvider();
        var request = MakeCallToolRequest(services);

        var expectedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }]
        };

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: request passed through, no exception
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ListTools_NoValidatorInDi_PassesThroughWithoutThrowing()
    {
        // Arrange: DI container with NO DownstreamTokenValidator registered
        var services = new ServiceCollection().BuildServiceProvider();
        var request = MakeListToolsRequest(services);

        var expectedResult = new ListToolsResult { Tools = [] };

        var filter = DownstreamAuthFilter.ListTools();
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next =
            (_, _) => new ValueTask<ListToolsResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: request passed through, no exception
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task CallTool_NullServices_PassesThroughWithoutThrowing()
    {
        // Arrange: Services is null (no DI scope at all)
        var request = MakeCallToolRequest(services: null);

        var expectedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }]
        };

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(expectedResult);
        var handler = filter(next);

        // Act
        var result = await handler(request, CancellationToken.None);

        // Assert: null Services treated as no validator — pass through
        Assert.Equal(expectedResult, result);
    }

    /// <summary>
    /// Verify that the error code constant is the value that the filter uses in exceptions.
    /// The filter formats errors as "{ErrorCode}: {reason}". This test confirms the constant
    /// is correctly placed and matches the expected value for Task 7 (gateway retry detection).
    /// </summary>
    [Fact]
    public void ErrorCodeConstant_HasExpectedValue()
    {
        // This constant is used by the filter in the McpException message
        // and will be referenced by gateway retry detection logic (Task 7).
        Assert.Equal("downstream_auth_required", DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired);
    }
}
