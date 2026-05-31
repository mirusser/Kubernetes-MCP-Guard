using InfraGate.Planner.Decision;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class OperationArgumentValidatorTests
{
    // ── restart_deployment ─────────────────────────────────────────────────────

    [Fact]
    public void TryNormalize_RestartDeployment_ValidArgs_ReturnsTrue()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
            },
            Reasoning: null);

        bool result = OperationArgumentValidator.TryNormalize(decision, out var args);

        Assert.True(result);
        Assert.Equal("nginx-demo", args[PlannerConventions.ToolArguments.Name]);
        Assert.Equal("mcp-nginx-demo", args[PlannerConventions.ToolArguments.Namespace]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_RestartDeployment_BlankName_ReturnsFalse(string name)
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = name,
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
            },
            Reasoning: null);

        bool result = OperationArgumentValidator.TryNormalize(decision, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryNormalize_RestartDeployment_MissingNamespace_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    // ── scale_deployment ───────────────────────────────────────────────────────

    [Fact]
    public void TryNormalize_ScaleDeployment_ValidIntReplicas_ReturnsTrue()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.ScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Replicas] = 3,
            },
            Reasoning: null);

        bool result = OperationArgumentValidator.TryNormalize(decision, out var args);

        Assert.True(result);
        Assert.Equal(3, args[PlannerConventions.ToolArguments.Replicas]);
    }

    [Fact]
    public void TryNormalize_ScaleDeployment_ZeroReplicas_ReturnsTrue()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.ScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Replicas] = 0,
            },
            Reasoning: null);

        Assert.True(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    [Fact]
    public void TryNormalize_ScaleDeployment_NegativeReplicas_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.ScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Replicas] = -1,
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    [Fact]
    public void TryNormalize_ScaleDeployment_LongReplicas_NormalizesToInt()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.ScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Replicas] = 2L,
            },
            Reasoning: null);

        bool result = OperationArgumentValidator.TryNormalize(decision, out var args);

        Assert.True(result);
        Assert.Equal(2, args[PlannerConventions.ToolArguments.Replicas]);
    }

    [Fact]
    public void TryNormalize_ScaleDeployment_MissingReplicas_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.ScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    // ── set_deployment_image ───────────────────────────────────────────────────

    [Fact]
    public void TryNormalize_SetDeploymentImage_ValidArgs_ReturnsTrue()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.SetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Container] = "nginx",
                [PlannerConventions.ToolArguments.Image] = "nginx:1.25",
            },
            Reasoning: null);

        bool result = OperationArgumentValidator.TryNormalize(decision, out var args);

        Assert.True(result);
        Assert.Equal("nginx", args[PlannerConventions.ToolArguments.Container]);
        Assert.Equal("nginx:1.25", args[PlannerConventions.ToolArguments.Image]);
    }

    [Fact]
    public void TryNormalize_SetDeploymentImage_MissingImage_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.SetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Container] = "nginx",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    [Fact]
    public void TryNormalize_SetDeploymentImage_MissingName_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.SetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Container] = "nginx",
                [PlannerConventions.ToolArguments.Image] = "nginx:1.25",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    [Fact]
    public void TryNormalize_SetDeploymentImage_BlankNamespace_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.SetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "",
                [PlannerConventions.ToolArguments.Container] = "nginx",
                [PlannerConventions.ToolArguments.Image] = "nginx:1.25",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    [Fact]
    public void TryNormalize_SetDeploymentImage_MissingContainer_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            PlannerConventions.OperationTypes.SetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
                [PlannerConventions.ToolArguments.Namespace] = "mcp-nginx-demo",
                [PlannerConventions.ToolArguments.Image] = "nginx:1.25",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }

    // ── unknown operation ──────────────────────────────────────────────────────

    [Fact]
    public void TryNormalize_UnknownOperationType_ReturnsFalse()
    {
        var decision = new RemediationDecision(
            "delete_resource",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.Name] = "nginx-demo",
            },
            Reasoning: null);

        Assert.False(OperationArgumentValidator.TryNormalize(decision, out _));
    }
}
