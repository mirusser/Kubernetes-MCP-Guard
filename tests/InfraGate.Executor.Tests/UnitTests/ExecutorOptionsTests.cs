using InfraGate.RuntimeSafety;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorOptionsTests
{
    [Fact]
    public void Validate_MissingGatewayBaseUrl_Throws()
    {
        var options = new ExecutorOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new ExecutorOptions { GatewayBaseUrl = "http://localhost:3001/mcp" };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpsMetadataDisabled_Throws()
    {
        var options = CreateProductionOptions() with
        {
            ClientCredentials = new()
            {
                Authority = "https://idp.example.com/realms/infra-gate",
                ClientId = ExecutorConventions.DefaultClientId,
                Scope = ExecutorConventions.DefaultOAuthScope,
                RequireHttpsMetadata = false,
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateProductionSafety(RuntimeMode.Production));

        Assert.Contains("RequireHttpsMetadata", exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpsMetadataEnabled_DoesNotThrow()
    {
        var options = CreateProductionOptions();

        var exception = Record.Exception(() => options.ValidateProductionSafety(RuntimeMode.Production));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateProductionSafety_WithDevelopmentMode_AllowsLocalMetadata()
    {
        var options = CreateProductionOptions() with
        {
            ClientCredentials = new()
            {
                Authority = "http://keycloak:8080/realms/infra-gate",
                ClientId = ExecutorConventions.DefaultClientId,
                Scope = ExecutorConventions.DefaultOAuthScope,
                RequireHttpsMetadata = false,
            }
        };

        var exception = Record.Exception(() => options.ValidateProductionSafety(RuntimeMode.Development));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(ExecutorConventions.MinConcurrencyCap - 1)]
    [InlineData(ExecutorConventions.MaxConcurrencyCap + 1)]
    public void Validate_ConcurrencyCapOutOfRange_Throws(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            ConcurrencyCap = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(ExecutorConventions.MinConcurrencyCap)]
    [InlineData(ExecutorConventions.MaxConcurrencyCap)]
    public void Validate_ConcurrencyCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            ConcurrencyCap = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(ExecutorConventions.MinWatchTimeoutSeconds - 1)]
    [InlineData(ExecutorConventions.MaxWatchTimeoutSeconds + 1)]
    public void Validate_WatchTimeoutSecondsOutOfRange_Throws(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WatchTimeoutSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(ExecutorConventions.MinWatchTimeoutSeconds)]
    [InlineData(ExecutorConventions.MaxWatchTimeoutSeconds)]
    public void Validate_WatchTimeoutSecondsAtBoundary_DoesNotThrow(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WatchTimeoutSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Defaults_AreWithinValidRange()
    {
        var options = new ExecutorOptions { GatewayBaseUrl = "http://localhost:3001/mcp" };

        Assert.InRange(options.ConcurrencyCap,
            ExecutorConventions.MinConcurrencyCap,
            ExecutorConventions.MaxConcurrencyCap);
        Assert.InRange(options.WatchTimeoutSeconds,
            ExecutorConventions.MinWatchTimeoutSeconds,
            ExecutorConventions.MaxWatchTimeoutSeconds);
    }

    private static ExecutorOptions CreateProductionOptions() =>
        new()
        {
            GatewayBaseUrl = "https://gateway.example.com/mcp",
            ClientCredentials = new()
            {
                Authority = "https://idp.example.com/realms/infra-gate",
                ClientId = ExecutorConventions.DefaultClientId,
                Scope = ExecutorConventions.DefaultOAuthScope,
                RequireHttpsMetadata = true,
            }
        };
}
