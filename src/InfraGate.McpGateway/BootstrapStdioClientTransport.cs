using System.Diagnostics;
using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

// Temporary transport shim for the downstream initialize auth gap.
// Remove this when McpClient.CreateAsync exposes initialize RequestOptions.Meta.
internal sealed class BootstrapStdioClientTransport(
    StdioClientTransportOptions options,
    string bootstrapLine,
    ILoggerFactory? loggerFactory = null) : IClientTransport
{
    private readonly ILogger? logger = loggerFactory?.CreateLogger<BootstrapStdioClientTransport>();

    private Process? process;

    public string Name => options.Name ?? options.Command;

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (process is not null)
        {
            throw new InvalidOperationException("The downstream stdio transport is already connected.");
        }

        logger?.LogInformation("Starting downstream stdio process: {Command} {Arguments}",
            options.Command,
            string.Join(" ", options.Arguments ?? []));

        var startedProcess = new Process
        {
            StartInfo = CreateStartInfo(options),
            EnableRaisingEvents = true
        };

        AttachStandardErrorDrain(startedProcess, options);

        if (!startedProcess.Start())
        {
            startedProcess.Dispose();
            throw new InvalidOperationException("The downstream stdio process could not be started.");
        }

        process = startedProcess;
        startedProcess.BeginErrorReadLine();
        logger?.LogInformation("Downstream stdio process started (PID={Pid}).", startedProcess.Id);

        try
        {
            logger?.LogInformation("Writing bootstrap line to downstream process stdin.");
            await startedProcess.StandardInput.WriteLineAsync(bootstrapLine.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await startedProcess.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            logger?.LogInformation("Connecting stream transport to downstream process.");
            var sessionTransport = await ConnectStreamTransportAsync(
                startedProcess.StandardOutput.BaseStream,
                startedProcess.StandardInput.BaseStream,
                loggerFactory,
                cancellationToken).ConfigureAwait(false);

            logger?.LogInformation("Downstream stream transport connected.");
            return new BootstrapStdioSessionTransport(
                sessionTransport,
                startedProcess,
                options.ShutdownTimeout);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to connect downstream stdio transport (PID={Pid}).", startedProcess.Id);
            await DisposeProcessAsync(startedProcess, options.ShutdownTimeout).ConfigureAwait(false);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(StdioClientTransportOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Command,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string argument in options.Arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach (var (name, value) in options.EnvironmentVariables)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        return startInfo;
    }

    private static void AttachStandardErrorDrain(Process process, StdioClientTransportOptions options)
    {
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                options.StandardErrorLines?.Invoke(args.Data);
            }
        };
    }

    // Extracted for testability: callers can verify stream ordering without spawning a real process.
    // serverOutput = stream to read from (process.StandardOutput.BaseStream)
    // serverInput  = stream to write to  (process.StandardInput.BaseStream)
    internal static async Task<ITransport> ConnectStreamTransportAsync(
        Stream serverOutput, Stream serverInput, ILoggerFactory? loggerFactory, CancellationToken ct)
    {
        // StreamClientTransport(arg1=write, arg2=read): arg1 is the write end, arg2 is wrapped in StreamReader.
        var transport = new StreamClientTransport(serverInput, serverOutput, loggerFactory);
        return await transport.ConnectAsync(ct).ConfigureAwait(false);
    }

    private static async ValueTask DisposeProcessAsync(Process process, TimeSpan shutdownTimeout)
    {
        CloseStandardInput(process);

        if (!process.HasExited)
        {
            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(shutdownTimeout, TimeProvider.System);
            var completedTask = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
            if (completedTask != exitTask && !process.HasExited)
            {
                KillProcess(process);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        process.Dispose();
    }

    private static void CloseStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (ObjectDisposedException)
        {
            // Justification: The process is already exiting; StandardInput close is benign.
        }
        catch (InvalidOperationException)
        {
            // Justification: The process has already exited; StandardInput close is benign.
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Justification: The process has already exited; Kill is benign.
        }
    }

    private sealed class BootstrapStdioSessionTransport(
        ITransport inner,
        Process process,
        TimeSpan shutdownTimeout) : ITransport
    {
        private int isDisposed;

        public string? SessionId => inner.SessionId;

        public ChannelReader<JsonRpcMessage> MessageReader => inner.MessageReader;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            {
                return;
            }

            await inner.DisposeAsync().ConfigureAwait(false);
            await DisposeProcessAsync(process, shutdownTimeout).ConfigureAwait(false);
        }

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
            inner.SendMessageAsync(message, cancellationToken);
    }
}
