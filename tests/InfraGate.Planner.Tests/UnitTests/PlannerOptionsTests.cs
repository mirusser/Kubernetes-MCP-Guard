using InfraGate.RuntimeSafety;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerOptionsTests
{
    [Fact]
    public void Validate_MissingGatewayBaseUrl_Throws()
    {
        var options = new PlannerOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_OpenRouterProvider_DoesNotRequireApiKey()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
        };

        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateProductionSafety_WithFreeOpenRouterRoute_Throws()
    {
        var options = CreateProductionOptions() with
        {
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmModel = "deepseek/deepseek-v4-flash:free"
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
                ClientId = PlannerConventions.DefaultClientId,
                Scope = PlannerConventions.DefaultOAuthScope,
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
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmModel = "openrouter/free",
            ClientCredentials = new()
            {
                Authority = "http://keycloak:8080/realms/infra-gate",
                ClientId = PlannerConventions.DefaultClientId,
                Scope = PlannerConventions.DefaultOAuthScope,
                RequireHttpsMetadata = false,
            }
        };

        var exception = Record.Exception(() => options.ValidateProductionSafety(RuntimeMode.Development));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds - 1)]
    [InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds + 1)]
    public void Validate_AnomalyWallClockCapOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            AnomalyWallClockCapSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinBatchWallClockCapSeconds - 1)]
    [InlineData(PlannerConventions.MaxBatchWallClockCapSeconds + 1)]
    public void Validate_BatchWallClockCapOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            BatchWallClockCapSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinMaxToolIterations - 1)]
    [InlineData(PlannerConventions.MaxMaxToolIterations + 1)]
    public void Validate_MaxToolIterationsOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            MaxToolIterations = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds)]
    [InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds)]
    public void Validate_AnomalyWallClockCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            AnomalyWallClockCapSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(PlannerConventions.MinBatchWallClockCapSeconds)]
    [InlineData(PlannerConventions.MaxBatchWallClockCapSeconds)]
    public void Validate_BatchWallClockCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            BatchWallClockCapSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(PlannerConventions.MinMaxToolIterations)]
    [InlineData(PlannerConventions.MaxMaxToolIterations)]
    public void Validate_MaxToolIterationsAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            MaxToolIterations = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Defaults_AreWithinValidRange()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
        };

        Assert.InRange(options.AnomalyWallClockCapSeconds,
            PlannerConventions.MinAnomalyWallClockCapSeconds,
            PlannerConventions.MaxAnomalyWallClockCapSeconds);
        Assert.InRange(options.BatchWallClockCapSeconds,
            PlannerConventions.MinBatchWallClockCapSeconds,
            PlannerConventions.MaxBatchWallClockCapSeconds);
        Assert.InRange(options.MaxToolIterations,
            PlannerConventions.MinMaxToolIterations,
            PlannerConventions.MaxMaxToolIterations);
    }

    [Fact]
    public void Validate_NonAiProvider_DoesNotThrow()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "other-provider",
        };

        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    private static PlannerOptions CreateProductionOptions() =>
        new()
        {
            GatewayBaseUrl = "https://gateway.example.com/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmModel = "anthropic/claude-sonnet-4.5",
            ClientCredentials = new()
            {
                Authority = "https://idp.example.com/realms/infra-gate",
                ClientId = PlannerConventions.DefaultClientId,
                Scope = PlannerConventions.DefaultOAuthScope,
                RequireHttpsMetadata = true,
            }
        };
}
