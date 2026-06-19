using InfraGate.RuntimeSafety;

namespace InfraGate.RuntimeSafety.Tests.UnitTests;

public sealed class ProductionSafetyValidatorTests
{
    private const string SettingName = "TestSetting";

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithNullValue_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri(null, SettingName));

        Assert.Contains(SettingName, exception.Message);
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithEmptyValue_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri(string.Empty, SettingName));

        Assert.Contains(SettingName, exception.Message);
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithNonAbsoluteUri_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri("not-a-uri", SettingName));

        Assert.Contains("absolute URI", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithHttpScheme_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri("http://example.com", SettingName));

        Assert.Contains("https", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithLoopbackHost_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri("https://127.0.0.1:3001", SettingName));

        Assert.Contains("loopback", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithHttpLoopback_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri("http://localhost", SettingName));

        Assert.Contains("https", exception.Message);
    }

    [Fact]
    public void RequireHttpsNonLoopbackUri_WithValidExternalHttpsUri_Succeeds()
    {
        Exception? exception = Record.Exception(
            () => ProductionSafetyValidator.RequireHttpsNonLoopbackUri("https://example.com", SettingName));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(ProductionSafetyValidator.OpenRouterFreeRoute)]
    [InlineData("deepseek/deepseek-v4-flash:free")]
    public void RequireExplicitNonDemoLlmRoute_WithFreeRoute_Throws(string model)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireExplicitNonDemoLlmRoute(
                "openrouter",
                model,
                "Provider",
                "Model"));

        Assert.Contains("free", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequireHttpsMetadataEnabled_WithFalseValue_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequireHttpsMetadataEnabled(false, SettingName));

        Assert.Contains(SettingName, exception.Message);
        Assert.Contains("true", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithImplicitPath_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                "/some/path", SettingName, isExplicit: false, new HashSet<string>()));

        Assert.Contains("explicitly configured", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithEmptyPath_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                string.Empty, SettingName, isExplicit: true, new HashSet<string>()));

        Assert.Contains("must not be empty", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithRelativePath_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                "relative/path", SettingName, isExplicit: true, new HashSet<string>()));

        Assert.Contains("absolute path", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithInvalidPath_Throws()
    {
        string root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString();
        string invalidPath = root.TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar +
                             "infra-gate\0invalid";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                invalidPath, SettingName, isExplicit: true, new HashSet<string>()));

        Assert.Contains("valid path", exception.Message);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void RequirePersistentDirectory_WithTempPath_Throws()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "infra-gate-test");
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                tempDir, SettingName, isExplicit: true, new HashSet<string>()));

        Assert.Contains("temp directory", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithDeniedDefaultName_Throws()
    {
        string deniedDir = Path.Combine("/var", "lib", "infra-gate", ".mcp-approvals");
        var deniedNames = new HashSet<string> { ".mcp-approvals" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                deniedDir, SettingName, isExplicit: true, deniedNames));

        Assert.Contains("default development directory", exception.Message);
    }

    [Fact]
    public void RequirePersistentDirectory_WithGroupWritableDirectory_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string dirPath = Path.Combine(Directory.GetCurrentDirectory(), $"infra-gate-gw-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dirPath);
        try
        {
            UnixFileMode currentMode = File.GetUnixFileMode(dirPath);
            File.SetUnixFileMode(dirPath, currentMode | UnixFileMode.GroupWrite);

            var exception = Assert.Throws<InvalidOperationException>(
                () => ProductionSafetyValidator.RequirePersistentDirectory(
                    dirPath, SettingName, isExplicit: true, new HashSet<string>()));

            Assert.Contains("group- or other-writable", exception.Message);
        }
        finally
        {
            if (Directory.Exists(dirPath))
            {
                File.SetUnixFileMode(dirPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(dirPath);
            }
        }
    }

    [Fact]
    public void RequirePersistentDirectory_WithValidPersistentPath_Succeeds()
    {
        string path = Path.Combine("/var", "lib", "infra-gate-prod");
        var deniedNames = new HashSet<string> { ".mcp-approvals" };

        Exception? exception = Record.Exception(
            () => ProductionSafetyValidator.RequirePersistentDirectory(
                path, SettingName, isExplicit: true, deniedNames));

        Assert.Null(exception);
    }
}
