namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class KubernetesMcpServerStartupValidatorTests : IDisposable
{
    private const string PinnedVersion = "v0.0.66";
    private const string Context = "minikube-mcp";

    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "infra-gate-k8s-mcp-startup-validator-tests",
        Guid.NewGuid().ToString("N"));

    public KubernetesMcpServerStartupValidatorTests()
    {
        Directory.CreateDirectory(testRoot);
    }

    [Fact]
    public void Validate_WithValidBundle_DoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        KubernetesMcpServerProcessOptions options = CreateValidBundle();

        KubernetesMcpServerStartupValidator.Validate(options);
    }

    [Fact]
    public void Validate_ExecutableMissing_ThrowsInvalidOperationException()
    {
        KubernetesMcpServerProcessOptions options = CreateValidBundle() with
        {
            Command = Path.Combine(testRoot, "does-not-exist"),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ExecutableNotExecutable_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string command = WriteExecutable("kubernetes-mcp-server-noexec", PinnedVersion);
        File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        WriteManifest(Path.GetDirectoryName(command)!, PinnedVersion);
        KubernetesMcpServerProcessOptions options = CreateValidBundle() with { Command = command };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("is not executable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ConfigFileMissing_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        KubernetesMcpServerProcessOptions options = CreateValidBundle() with
        {
            Arguments = ["--config", "no-such-config.toml"],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_KubeconfigGroupOrOtherWritable_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        KubernetesMcpServerProcessOptions options = CreateValidBundle();
        string kubeconfigPath = Path.GetFullPath(options.Kubeconfig, Path.GetFullPath(options.WorkingDirectory));
        UnixFileMode currentMode = File.GetUnixFileMode(kubeconfigPath);
        File.SetUnixFileMode(kubeconfigPath, currentMode | UnixFileMode.GroupWrite);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                KubernetesMcpServerStartupValidator.Validate(options));

            Assert.Contains("group- or other-writable", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(kubeconfigPath, currentMode);
        }
    }

    [Fact]
    public void Validate_ManifestMissing_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string isolatedDirectory = Path.Combine(testRoot, "no-manifest");
        Directory.CreateDirectory(isolatedDirectory);
        string command = WriteExecutable(isolatedDirectory, "kubernetes-mcp-server", PinnedVersion);
        KubernetesMcpServerProcessOptions options = CreateValidBundle() with { Command = command };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("manifest was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReportedVersionMismatch_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string command = WriteExecutable("kubernetes-mcp-server-wrong-version", "v9.9.9");
        WriteManifest(Path.GetDirectoryName(command)!, PinnedVersion);
        KubernetesMcpServerProcessOptions options = CreateValidBundle() with { Command = command };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("reports version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EnabledToolsDoNotMatchPolicy_ThrowsInvalidOperationException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string configPath = WriteTomlConfig("mismatched.toml", ["pods_get", "pods_delete"]);
        KubernetesMcpServerProcessOptions options = CreateValidBundle() with
        {
            Arguments = ["--config", configPath],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerStartupValidator.Validate(options));

        Assert.Contains("does not match the approved read-only tool policy", exception.Message, StringComparison.Ordinal);
    }

    private KubernetesMcpServerProcessOptions CreateValidBundle()
    {
        string command = WriteExecutable("kubernetes-mcp-server", PinnedVersion);
        WriteManifest(Path.GetDirectoryName(command)!, PinnedVersion);
        string configPath = WriteTomlConfig(
            "k8s-mcp.toml",
            McpGatewayConventions.SecondaryDownstream.ApprovedTools);
        string kubeconfigPath = WriteKubeconfig("viewer.config");

        return new KubernetesMcpServerProcessOptions(
            command,
            ["--config", configPath],
            testRoot,
            kubeconfigPath,
            Context,
            new HashSet<string>(StringComparer.Ordinal) { "mcp-nginx-demo" });
    }

    private string WriteExecutable(string fileName, string reportedVersion) =>
        WriteExecutable(testRoot, fileName, reportedVersion);

    private static string WriteExecutable(string directory, string fileName, string reportedVersion)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, $"#!/usr/bin/env bash\necho \"{reportedVersion}\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static void WriteManifest(string directory, string version)
    {
        string path = Path.Combine(directory, McpGatewayConventions.SecondaryDownstream.ManifestFileName);
        File.WriteAllText(
            path,
            "{\"version\": \"" + version + "\", \"checksums\": {\"linux-amd64\": \"deadbeef\"}}");
    }

    private string WriteTomlConfig(string fileName, IEnumerable<string> enabledTools)
    {
        string path = Path.Combine(testRoot, fileName);
        string toolList = string.Join(", ", enabledTools.Select(tool => $"\"{tool}\""));
        File.WriteAllText(path, $"enabled_tools = [{toolList}]\n");
        return path;
    }

    private string WriteKubeconfig(string fileName)
    {
        string path = Path.Combine(testRoot, fileName);
        File.WriteAllText(
            path,
            "apiVersion: v1\n" +
            "kind: Config\n" +
            "clusters:\n" +
            "- name: demo\n" +
            "  cluster:\n" +
            "    server: https://127.0.0.1\n" +
            "contexts:\n" +
            $"- name: {Context}\n" +
            "  context:\n" +
            "    cluster: demo\n" +
            "    user: viewer\n" +
            $"current-context: {Context}\n" +
            "users:\n" +
            "- name: viewer\n" +
            "  user:\n" +
            "    token: test-token\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return path;
    }

    public void Dispose()
    {
        Directory.Delete(testRoot, recursive: true);
    }
}
