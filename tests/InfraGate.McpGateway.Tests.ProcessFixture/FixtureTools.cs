using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway.Tests.ProcessFixture;

[McpServerToolType]
public static class FixtureTools
{
    [McpServerTool(Name = "ping", ReadOnly = true, OpenWorld = false)]
    [Description("Returns pong. Used by DownstreamProcessSupervisor tests to probe process liveness.")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "echo-meta", ReadOnly = true, OpenWorld = false)]
    [Description("Echoes the request's _meta object as JSON. Used to verify W3C trace context propagation into downstream MCP requests.")]
    public static string EchoMeta(RequestContext<CallToolRequestParams> context) =>
        (context.Params?.Meta ?? []).ToJsonString();

    [McpServerTool(Name = "fail", ReadOnly = true, OpenWorld = false)]
    [Description("Always returns an IsError=true result. Used to verify MCP-error outcome telemetry.")]
    public static CallToolResult Fail() => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = "simulated tool failure" }]
    };
}
