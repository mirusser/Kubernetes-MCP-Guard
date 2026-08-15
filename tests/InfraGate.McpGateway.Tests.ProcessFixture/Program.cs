using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway.Tests.ProcessFixture;

// Real, controllable stdio MCP server used only by DownstreamProcessSupervisor tests
// (Task 12). Never write anything but MCP protocol frames to stdout — all host logging is
// disabled and diagnostics, if any, must go to stderr only.
//
// Controlled via a directory of plain-text files (path passed as --control-dir):
//   spawn-count.txt          incremented on every process start, before anything else runs.
//   fail-startup-count.txt   if present and > 0, this start decrements it and the process
//                            exits(17) immediately, without ever starting the MCP transport.
//   command.txt              polled every 25ms while running; consumed (deleted) once read.
//                            "exit"  -> graceful Environment.Exit(0).
//                            "crash" -> Environment.FailFast (abrupt, unflushed termination;
//                                       the closest a same-process fixture can get to a real
//                                       broken pipe).
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string controlDir = ParseControlDir(args);
        Directory.CreateDirectory(controlDir);

        RecordSpawn(Path.Combine(controlDir, "spawn-count.txt"));
        await File.WriteAllTextAsync(
            Path.Combine(controlDir, "pid.txt"),
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

        if (TryConsumeFailStartup(Path.Combine(controlDir, "fail-startup-count.txt")))
        {
            return 17;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        using IHost host = builder.Build();

        using var shutdownCts = new CancellationTokenSource();
        Task commandPollTask = PollCommandsAsync(Path.Combine(controlDir, "command.txt"), shutdownCts.Token);

        await host.RunAsync(shutdownCts.Token).ConfigureAwait(false);

        await shutdownCts.CancelAsync().ConfigureAwait(false);
        await commandPollTask.ConfigureAwait(false);
        return 0;
    }

    private static string ParseControlDir(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--control-dir", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        throw new InvalidOperationException("--control-dir <path> is required.");
    }

    private static void RecordSpawn(string path)
    {
        WithRetry(() =>
        {
            int current = TryReadInt(path, out int parsed) ? parsed : 0;
            File.WriteAllText(path, (current + 1).ToString(CultureInfo.InvariantCulture));
        });
    }

    private static bool TryConsumeFailStartup(string path)
    {
        bool consumed = false;
        WithRetry(() =>
        {
            if (!TryReadInt(path, out int remaining) || remaining <= 0)
            {
                return;
            }

            File.WriteAllText(path, (remaining - 1).ToString(CultureInfo.InvariantCulture));
            consumed = true;
        });

        return consumed;
    }

    private static bool TryReadInt(string path, out int value)
    {
        if (File.Exists(path) &&
            int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    private static void WithRetry(Action action)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(10);
            }
        }
    }

    private static async Task PollCommandsAsync(string path, CancellationToken shutdownToken)
    {
        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(25, shutdownToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string? command = TryReadAndClearCommand(path);
            switch (command)
            {
                case "exit":
                    Environment.Exit(0);
                    return;
                case "crash":
                    Environment.FailFast("Controllable fixture: simulated crash.");
                    return;
            }
        }
    }

    private static string? TryReadAndClearCommand(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
