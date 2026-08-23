using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

/// <summary>
/// Typed result from a downstream MCP tool call that preserves all MCP result semantics.
/// </summary>
public sealed record class DownstreamCallResult(
    IReadOnlyList<object> Content,
    bool IsError,
    JsonObject? Meta,
    bool IsTransportFault = false)
{
    /// <summary>
    /// Creates a result from a successful MCP call.
    /// </summary>
    public static DownstreamCallResult FromCallToolResult(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // result.Content is IReadOnlyList<Content> from the MCP SDK, where Content is the base class
        // We store it as IReadOnlyList<object> to avoid tight coupling to SDK internal types
        return new DownstreamCallResult(
            result.Content.Cast<object>().ToList(),
            result.IsError == true,
            result.Meta);
    }

    /// <summary>
    /// Creates an error result from a transport exception. <see cref="IsTransportFault"/> is set so
    /// a <see cref="DownstreamProcessSupervisor"/> wrapping the client can detect the fault and
    /// trigger a restart without needing the swallowed exception itself.
    /// </summary>
    public static DownstreamCallResult FromTransportException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new DownstreamCallResult(
            [new TextContentBlock { Text = $"(Transport error) {exception.GetType().Name}: {exception.Message}" }],
            IsError: true,
            Meta: null,
            IsTransportFault: true);
    }

    /// <summary>
    /// Creates a simple success result from text content. For testing and simple cases.
    /// </summary>
    public static DownstreamCallResult FromText(string text)
    {
        return new DownstreamCallResult(
            [new TextContentBlock { Text = text }],
            IsError: false,
            Meta: null);
    }
}
