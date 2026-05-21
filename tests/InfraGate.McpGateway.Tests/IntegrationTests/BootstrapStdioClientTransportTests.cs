using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.IntegrationTests;

/// <summary>
/// Verifies that BootstrapStdioClientTransport wires process.StandardOutput.BaseStream as the
/// read end and process.StandardInput.BaseStream as the write end of the MCP session transport.
///
/// The swap (StandardInput↔StandardOutput reversed) produces a 30-second timeout at runtime
/// but no compile-time error and no unit-test failure because the bug is structural plumbing
/// rather than logic. These tests use in-process pipes to make the stream directions observable.
///
/// Framing note: StreamClientTransport uses newline-delimited JSON (JSON object + '\n'),
/// not HTTP-style Content-Length headers.
/// </summary>
public sealed class BootstrapStdioClientTransportTests
{
    [Fact]
    public async Task ConnectStreamTransportAsync_CompletesInitializeHandshake_WhenStreamsAreOrderedCorrectly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // serverToClient: server writes, client reads  (= process.StandardOutput.BaseStream)
        // clientToServer: client writes, server reads  (= process.StandardInput.BaseStream)
        var serverToClient = new Pipe();
        var clientToServer = new Pipe();

        var serverReadStream = clientToServer.Reader.AsStream();
        var serverWriteStream = serverToClient.Writer.AsStream();
        var clientReadStream = serverToClient.Reader.AsStream();    // CanRead = true
        var clientWriteStream = clientToServer.Writer.AsStream();   // CanWrite = true

        // Start the fake MCP server. It reads from clientToServer and writes to serverToClient.
        // CA2025: serverReadStream / serverWriteStream are not enclosed in a using scope — they
        // outlive the task and are completed explicitly via PipeWriter.CompleteAsync below.
#pragma warning disable CA2025
        var serverTask = ServeMcpInitializeAsync(serverReadStream, serverWriteStream, cts.Token);
#pragma warning restore CA2025

        // clientReadStream  = equivalent of process.StandardOutput.BaseStream → serverOutput param
        // clientWriteStream = equivalent of process.StandardInput.BaseStream  → serverInput param
        var sessionTransport = await BootstrapStdioClientTransport.ConnectStreamTransportAsync(
            serverOutput: clientReadStream,
            serverInput: clientWriteStream,
            loggerFactory: null,
            ct: cts.Token);

        await using var client = await McpClient.CreateAsync(
            new OneTimeTransport(sessionTransport),
            cancellationToken: cts.Token);

        Assert.NotNull(client);

        await serverToClient.Writer.CompleteAsync();
        await clientToServer.Writer.CompleteAsync();
        await serverTask;
    }

    // Minimal MCP JSON-RPC server that handles one initialize + notifications/initialized exchange.
    private static async Task ServeMcpInitializeAsync(
        Stream serverInput, Stream serverOutput, CancellationToken ct)
    {
        string requestBody = await ReadMessageAsync(serverInput, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No initialize request received.");

        using var doc = JsonDocument.Parse(requestBody);
        JsonNode? idNode = JsonNode.Parse(doc.RootElement.GetProperty("id").GetRawText());

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode,
            ["result"] = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "test-server",
                    ["version"] = "0.0.1"
                }
            }
        };

        await WriteMessageAsync(serverOutput, response.ToJsonString(), ct).ConfigureAwait(false);

        // Drain the notifications/initialized message; no reply is expected.
        await ReadMessageAsync(serverInput, ct).ConfigureAwait(false);
    }

    // StreamClientTransport uses newline-delimited JSON: each message is a JSON object + '\n'.
    private static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        byte[] oneByte = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(oneByte.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            char c = (char)oneByte[0];
            if (c == '\n')
            {
                break;
            }

            if (c != '\r')
            {
                sb.Append(c);
            }
        }

        string line = sb.ToString();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private static async Task WriteMessageAsync(Stream stream, string message, CancellationToken ct)
    {
        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
        await stream.WriteAsync(data.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // Wraps a pre-connected ITransport so McpClient.CreateAsync can accept it.
    // McpClient expects an IClientTransport whose ConnectAsync returns the session transport.
    private sealed class OneTimeTransport(ITransport sessionTransport) : IClientTransport
    {
        public string Name => "test";

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(sessionTransport);
    }
}
