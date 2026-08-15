using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

// Startup-only validation for the secondary, read-only-only kubernetes-mcp-server downstream,
// invoked once from RegisterKubernetesMcpServerDownstream after
// KubernetesMcpServerProcessOptions.FromConfiguration succeeds (see docs/adr for the decision
// record). Deliberately NOT folded into FromConfiguration: FromConfiguration must stay pure and
// synchronous (no subprocess spawning, no real executable required) so it keeps working with the
// fixture Command values KubernetesMcpServerProcessOptionsTests uses. These checks touch the
// filesystem and run the configured binary, so they belong in the real composition root only.
internal static partial class KubernetesMcpServerStartupValidator
{
    private static readonly UnixFileMode GroupOrOtherWrite =
        UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    public static void Validate(KubernetesMcpServerProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string workingDirectory = Path.GetFullPath(options.WorkingDirectory);
        string configPath = Path.GetFullPath(options.Arguments[1], workingDirectory);
        string kubeconfigPath = Path.GetFullPath(options.Kubeconfig, workingDirectory);

        ValidateExecutable(options.Command);
        ValidateConfigExists(configPath);
        ValidateKubeconfigNotWritable(kubeconfigPath);
        ValidateReportedVersion(options.Command);
        ValidateEnabledToolsMatchPolicy(configPath);
    }

    private static void ValidateExecutable(string command)
    {
        if (!File.Exists(command))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server executable '{command}' does not exist.");
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!File.GetUnixFileMode(command).HasFlag(UnixFileMode.UserExecute))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server executable '{command}' is not executable.");
        }
    }

    private static void ValidateConfigExists(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server configuration file '{configPath}' does not exist.");
        }
    }

    private static void ValidateKubeconfigNotWritable(string kubeconfigPath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(kubeconfigPath))
        {
            return;
        }

        if ((File.GetUnixFileMode(kubeconfigPath) & GroupOrOtherWrite) != 0)
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP secondary kubeconfig '{kubeconfigPath}' must not be group- or other-writable.");
        }
    }

    private static void ValidateReportedVersion(string command)
    {
        string commandDirectory = Path.GetDirectoryName(Path.GetFullPath(command))
            ?? throw new InvalidOperationException(
                $"Kubernetes MCP server executable '{command}' has no parent directory.");
        string manifestPath = Path.Combine(
            commandDirectory,
            McpGatewayConventions.SecondaryDownstream.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server manifest was not found at '{manifestPath}'.");
        }

        string expectedVersion = ReadPinnedVersion(manifestPath);
        string reportedVersion = RunVersionCommand(command);
        if (!string.Equals(reportedVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server '{command}' reports version '{reportedVersion}', expected '{expectedVersion}'.");
        }
    }

    private static string ReadPinnedVersion(string manifestPath)
    {
        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("version", out JsonElement versionElement)
            || versionElement.GetString() is not { Length: > 0 } version)
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server manifest '{manifestPath}' does not declare a version.");
        }

        return version;
    }

    private static string RunVersionCommand(string command)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(McpGatewayConventions.SecondaryDownstream.VersionArgument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Failed to start Kubernetes MCP server '{command}' to check its version.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string combined = stdout.Length > 0 ? stdout : stderr;
        return WhitespacePattern().Replace(combined, string.Empty);
    }

    private static void ValidateEnabledToolsMatchPolicy(string configPath)
    {
        string tomlContent = File.ReadAllText(configPath);
        Match match = EnabledToolsPattern().Match(tomlContent);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server configuration '{configPath}' does not declare enabled_tools.");
        }

        IReadOnlySet<string> declaredTools = match.Groups["names"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        if (!declaredTools.SetEquals(McpGatewayConventions.SecondaryDownstream.ApprovedTools))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server configuration '{configPath}' enabled_tools does not match the approved read-only tool policy.");
        }
    }

    [GeneratedRegex(@"enabled_tools\s*=\s*\[(?<names>[^\]]*)\]", RegexOptions.None,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex EnabledToolsPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex WhitespacePattern();
}
