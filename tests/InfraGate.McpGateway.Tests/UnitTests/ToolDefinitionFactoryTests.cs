using System.Text;
using System.Text.Json;
using InfraGate.McpGateway;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ToolDefinitionFactoryTests
{
    private static readonly JsonElement ValidSchema = JsonSerializer.Deserialize<JsonElement>(
        """{"type":"object","properties":{}}""");

    [Fact]
    public void CreateForwardedTool_IsReadOnlyFalse_AnnotationsAreNull()
    {
        var dt = new DownstreamTool("test-tool", "A test tool", IsReadOnly: false, IsDestructive: true, InputSchema: ValidSchema);
        var result = ToolDefinitionFactory.CreateForwardedTool(dt);

        Assert.Equal("test-tool", result.Name);
        Assert.Null(result.Annotations);
    }

    [Fact]
    public void CreateForwardedTool_IsReadOnlyTrue_AnnotationsHaveReadOnlyHint()
    {
        var dt = new DownstreamTool("readonly-tool", "A read-only tool", IsReadOnly: true, IsDestructive: false, InputSchema: ValidSchema);
        var result = ToolDefinitionFactory.CreateForwardedTool(dt);

        Assert.Equal("readonly-tool", result.Name);
        Assert.NotNull(result.Annotations);
        Assert.True(result.Annotations.ReadOnlyHint);
    }
}
