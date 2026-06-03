using InfraGate.RuntimeSafety;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_DoesNotThrow()
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp"
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_MissingGatewayBaseUrl_Throws()
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = string.Empty
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_WhitespaceGatewayBaseUrl_Throws(string url)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = url
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4000)]
    public void Validate_CadenceOutOfBounds_Throws(int cadenceSeconds)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            CycleIntervalSeconds = cadenceSeconds
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(3600)]
    public void Validate_CadenceWithinBounds_DoesNotThrow(int cadenceSeconds)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            CycleIntervalSeconds = cadenceSeconds
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void DefaultOptions_UseConventionConstants()
    {
        var options = new ObserverOptions();

        Assert.Equal(AnomalyObserverConventions.DefaultCadenceSeconds, options.CycleIntervalSeconds);
        Assert.Equal(AnomalyObserverConventions.WallClockCapSeconds, options.WallClockCapSeconds);
        Assert.Equal(AnomalyObserverConventions.MaxToolIterations, options.MaxToolIterations);
    }

    [Fact]
    public void ValidateProductionSafety_WithFreeOpenRouterRoute_Throws()
    {
        var options = CreateProductionOptions() with
        {
            LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
            LlmModel = "openrouter/free"
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateProductionSafety(RuntimeMode.Production));

        Assert.Contains("free", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpsMetadataDisabled_Throws()
    {
        var options = CreateProductionOptions() with
        {
            ClientCredentials = new()
            {
                Authority = "https://idp.example.com/realms/infra-gate",
                ClientId = ObserverConventions.DefaultClientId,
                Scope = ObserverConventions.DefaultOAuthScope,
                RequireHttpsMetadata = false,
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateProductionSafety(RuntimeMode.Production));

        Assert.Contains("RequireHttpsMetadata", exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithExplicitNonDemoRouteAndHttpsMetadata_DoesNotThrow()
    {
        var options = CreateProductionOptions();

        var exception = Record.Exception(() => options.ValidateProductionSafety(RuntimeMode.Production));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateProductionSafety_WithDevelopmentMode_AllowsLocalDemoSettings()
    {
        var options = CreateProductionOptions() with
        {
            LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
            LlmModel = "deepseek/deepseek-v4-flash:free",
            ClientCredentials = new()
            {
                Authority = "http://keycloak:8080/realms/infra-gate",
                ClientId = ObserverConventions.DefaultClientId,
                Scope = ObserverConventions.DefaultOAuthScope,
                RequireHttpsMetadata = false,
            }
        };

        var exception = Record.Exception(() => options.ValidateProductionSafety(RuntimeMode.Development));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(301)]
    public void Validate_WallClockCapOutOfBounds_Throws(int wallClockCapSeconds)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WallClockCapSeconds = wallClockCapSeconds
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(120)]
    [InlineData(300)]
    public void Validate_WallClockCapWithinBounds_DoesNotThrow(int wallClockCapSeconds)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WallClockCapSeconds = wallClockCapSeconds
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Validate_MaxToolIterationsOutOfBounds_Throws(int maxToolIterations)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            MaxToolIterations = maxToolIterations
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(20)]
    public void Validate_MaxToolIterationsWithinBounds_DoesNotThrow(int maxToolIterations)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            MaxToolIterations = maxToolIterations
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Validate_DedupeSuppressionWindowOutOfBounds_Throws(int window)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            DedupeSuppressionWindow = window
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    public void Validate_DedupeSuppressionWindowWithinBounds_DoesNotThrow(int window)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            DedupeSuppressionWindow = window
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Validate_DedupeResolutionThresholdOutOfBounds_Throws(int threshold)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            DedupeResolutionThreshold = threshold
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void Validate_DedupeResolutionThresholdWithinBounds_DoesNotThrow(int threshold)
    {
        var options = new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            DedupeResolutionThreshold = threshold
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    private static ObserverOptions CreateProductionOptions() =>
        new()
        {
            GatewayBaseUrl = "https://gateway.example.com/mcp",
            LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
            LlmModel = "anthropic/claude-sonnet-4.5",
            ClientCredentials = new()
            {
                Authority = "https://idp.example.com/realms/infra-gate",
                ClientId = ObserverConventions.DefaultClientId,
                Scope = ObserverConventions.DefaultOAuthScope,
                RequireHttpsMetadata = true,
            }
        };
}
