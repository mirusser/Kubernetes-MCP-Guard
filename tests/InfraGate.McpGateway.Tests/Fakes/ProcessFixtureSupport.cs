using System.Globalization;

namespace InfraGate.McpGateway.Tests.Fakes;

/// <summary>
/// Resolves the built fixture executable's own output directory. A <c>ProjectReference</c>
/// only copies a referenced Exe project's primary artifacts (dll/deps.json/runtimeconfig.json)
/// into this test project's output -- not that project's own transitive package dependencies
/// (e.g. Microsoft.Extensions.Hosting.*) -- so the fixture must be launched from its own build
/// output directory, which does contain its full dependency closure.
/// </summary>
internal static class ProcessFixtureLocator
{
    internal static string ResolveDllPath()
    {
        DirectoryInfo tfmDir = new(AppContext.BaseDirectory);
        string tfm = tfmDir.Name;
        string configuration = tfmDir.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine build configuration from test output path.");
        DirectoryInfo? testsRoot = tfmDir.Parent?.Parent?.Parent?.Parent;
        if (testsRoot is null)
        {
            throw new InvalidOperationException("Could not locate the 'tests' directory from test output path.");
        }

        string dllPath = Path.Combine(
            testsRoot.FullName,
            "InfraGate.McpGateway.Tests.ProcessFixture",
            "bin",
            configuration,
            tfm,
            "InfraGate.McpGateway.Tests.ProcessFixture.dll");

        if (!File.Exists(dllPath))
        {
            throw new InvalidOperationException(
                $"Process fixture not found at '{dllPath}'. Build the solution before running these tests.");
        }

        return dllPath;
    }
}

internal static class ControlFile
{
    internal static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "downstream-fixture-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void WriteCommand(string controlDir, string command) =>
        File.WriteAllText(Path.Combine(controlDir, "command.txt"), command);

    internal static void SetFailStartupCount(string controlDir, int count) =>
        File.WriteAllText(
            Path.Combine(controlDir, "fail-startup-count.txt"),
            count.ToString(CultureInfo.InvariantCulture));

    internal static int ReadSpawnCount(string controlDir)
    {
        string path = Path.Combine(controlDir, "spawn-count.txt");
        return File.Exists(path) ? int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture) : 0;
    }

    internal static int ReadPid(string controlDir)
    {
        string path = Path.Combine(controlDir, "pid.txt");
        return int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture);
    }
}
