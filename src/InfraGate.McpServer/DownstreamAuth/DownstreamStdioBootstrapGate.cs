using InfraGate.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace InfraGate.McpServer.DownstreamAuth;

internal static class DownstreamStdioBootstrapGate
{
    private static readonly TimeSpan BootstrapReadTimeout = TimeSpan.FromSeconds(5);

    internal static async Task<bool> ValidateAsync(
        IServiceProvider services,
        Stream input,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var validator = services.GetService<DownstreamTokenValidator>();
        if (validator is null)
        {
            return true;
        }

        string? bootstrapLine = await ReadBootstrapLineAsync(input, cancellationToken).ConfigureAwait(false);
        string? token = ExtractBearerToken(bootstrapLine);
        var result = await validator.ValidateAsync(token ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (result.IsValid)
        {
            logger.LogInformation("Downstream bootstrap auth validated.");
            return true;
        }

        logger.LogWarning("Downstream bootstrap auth rejected: {Reason}", result.FailureReason);
        return false;
    }

    internal static string? ExtractBearerToken(string? bootstrapLine)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLine))
        {
            return null;
        }

        int separatorIndex = bootstrapLine.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return null;
        }

        string key = bootstrapLine[..separatorIndex].Trim();
        if (!string.Equals(key, DownstreamAuthConventions.BootstrapLineKey, StringComparison.Ordinal))
        {
            return null;
        }

        string value = bootstrapLine[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith(DownstreamAuthConventions.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value[DownstreamAuthConventions.BearerPrefix.Length..].Trim();
        }

        return value;
    }

    private static async Task<string?> ReadBootstrapLineAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BootstrapReadTimeout);

        try
        {
            return await ReadLineWithoutReadAheadAsync(input, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> ReadLineWithoutReadAheadAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        const int maxBootstrapLineBytes = 16 * 1024;
        using var line = new MemoryStream();
        byte[] buffer = new byte[1];

        // Read from raw stdin one byte at a time. Console.In/TextReader can buffer
        // past the newline and steal the following initialize JSON from the SDK.
        while (line.Length < maxBootstrapLineBytes)
        {
            int bytesRead = await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return line.Length == 0 ? null : Encoding.UTF8.GetString(line.ToArray());
            }

            if (buffer[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
            }

            line.WriteByte(buffer[0]);
        }

        return null;
    }
}
