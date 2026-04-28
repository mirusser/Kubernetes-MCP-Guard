using InfraGate.McpGateway;
using Microsoft.AspNetCore.Http;

namespace InfraGate.McpGateway.Tests;

public sealed class BearerTokenMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RejectsMissingBearerTokenForMcp()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task InvokeAsync_RejectsWrongBearerTokenForMcp()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = "Bearer wrong";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task InvokeAsync_AllowsValidBearerTokenForMcp()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = "Bearer secret";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsNonMcpPathsWithoutToken()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    private static BearerTokenMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            new McpGatewayOptions(
                "secret",
                "downstream.csproj",
                Path.Combine(Path.GetTempPath(), "infra-gate-guard-tests"),
                Directory.GetCurrentDirectory()));
}
