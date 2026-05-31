namespace InfraGate.Prompts.Tests.UnitTests;

public sealed class SemanticKernelPromptLibraryTests
{
    private static IPromptLibrary BuildLibrary(Action<PromptLibraryBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(configure);
        return services.BuildServiceProvider().GetRequiredService<IPromptLibrary>();
    }

    [Fact]
    public async Task RenderAsync_AllArgsProvided_SubstitutesTokens()
    {
        var library = BuildLibrary(b => b.AddTemplate(
            "test",
            "Hello {{name}}, you have {{count}} items.",
            ["name", "count"]));

        var result = await library.RenderAsync("test", new Dictionary<string, object?>
        {
            ["name"] = "World",
            ["count"] = 42,
        });

        Assert.Equal("Hello World, you have 42 items.", result);
    }

    [Fact]
    public async Task RenderAsync_NoVariables_ReturnsTemplateVerbatim()
    {
        const string staticText = "This prompt has no placeholders.";
        var library = BuildLibrary(b => b.AddTemplate("static", staticText));

        var result = await library.RenderAsync("static", new Dictionary<string, object?>());

        Assert.Equal(staticText, result);
    }

    [Fact]
    public async Task RenderAsync_UnknownTemplate_ThrowsKeyNotFoundException()
    {
        var library = BuildLibrary(b => b.AddTemplate("known", "text"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            library.RenderAsync("unknown", new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task RenderAsync_MissingRequiredArg_ThrowsArgumentException()
    {
        var library = BuildLibrary(b => b.AddTemplate(
            "t",
            "Hello {{name}}.",
            ["name"]));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            library.RenderAsync("t", new Dictionary<string, object?>()));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_DifferentValues_ProduceDifferentOutput()
    {
        var library = BuildLibrary(b => b.AddTemplate(
            "ns-prompt",
            "Observer for namespace {{namespace}}.",
            ["namespace"]));

        var resultA = await library.RenderAsync("ns-prompt", new Dictionary<string, object?> { ["namespace"] = "ns-a" });
        var resultB = await library.RenderAsync("ns-prompt", new Dictionary<string, object?> { ["namespace"] = "ns-b" });

        Assert.NotEqual(resultA, resultB, StringComparer.Ordinal);
        Assert.Contains("ns-a", resultA, StringComparison.Ordinal);
        Assert.Contains("ns-b", resultB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_IntegerArgument_RendersAsString()
    {
        var library = BuildLibrary(b => b.AddTemplate(
            "t",
            "Max iterations: {{maxIter}}.",
            ["maxIter"]));

        var result = await library.RenderAsync("t", new Dictionary<string, object?> { ["maxIter"] = 8 });

        Assert.Equal("Max iterations: 8.", result);
    }

    [Fact]
    public async Task RenderAsync_IsDeterministicForSameInput()
    {
        var library = BuildLibrary(b => b.AddTemplate(
            "t",
            "ns={{ns}} iter={{iter}}",
            ["ns", "iter"]));

        var args = new Dictionary<string, object?> { ["ns"] = "default", ["iter"] = 5 };

        var first = await library.RenderAsync("t", args);
        var second = await library.RenderAsync("t", args);

        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_MultipleTemplates_EachRendersIndependently()
    {
        var library = BuildLibrary(b => b
            .AddTemplate("a", "template-a: {{x}}", ["x"])
            .AddTemplate("b", "template-b: {{y}}", ["y"]));

        var resultA = await library.RenderAsync("a", new Dictionary<string, object?> { ["x"] = "foo" });
        var resultB = await library.RenderAsync("b", new Dictionary<string, object?> { ["y"] = "bar" });

        Assert.Equal("template-a: foo", resultA);
        Assert.Equal("template-b: bar", resultB);
    }

    [Fact]
    public async Task RenderAsync_JsonLiteralInTemplate_PassesThrough()
    {
        const string template = """
            Return JSON like: {"key": "value"}
            For namespace {{namespace}}.
            """;

        var library = BuildLibrary(b => b.AddTemplate("json-test", template, ["namespace"]));

        var result = await library.RenderAsync("json-test",
            new Dictionary<string, object?> { ["namespace"] = "default" });

        Assert.Contains("{\"key\": \"value\"}", result, StringComparison.Ordinal);
        Assert.Contains("default", result, StringComparison.Ordinal);
    }
}
