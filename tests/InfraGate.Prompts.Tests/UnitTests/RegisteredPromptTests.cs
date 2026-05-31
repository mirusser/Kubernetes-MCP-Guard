namespace InfraGate.Prompts.Tests.UnitTests;

public sealed class RegisteredPromptTests
{
    private static RegisteredPrompt CreatePrompt(
        string name = "test",
        string templateText = "Hello {{name}}.",
        IReadOnlyList<string>? requiredVariables = null)
    {
        var builder = new PromptLibraryBuilder();
        builder.AddTemplate(name, templateText, requiredVariables);
        var templates = builder.Build();
        return templates[name];
    }

    [Fact]
    public void ValidateRequired_AllProvided_DoesNotThrow()
    {
        var prompt = CreatePrompt(requiredVariables: ["name", "count"]);

        var args = new Dictionary<string, object?> { ["name"] = "x", ["count"] = 1 };

        var ex = Record.Exception(() => prompt.ValidateRequired(args));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateRequired_MissingVariable_ThrowsArgumentException()
    {
        var prompt = CreatePrompt(requiredVariables: ["name", "namespace"]);

        var args = new Dictionary<string, object?> { ["name"] = "x" };

        var ex = Assert.Throws<ArgumentException>(() => prompt.ValidateRequired(args));
        Assert.Contains("namespace", ex.Message, StringComparison.Ordinal);
        Assert.Equal("arguments", ex.ParamName);
    }

    [Fact]
    public void ValidateRequired_MultipleMissing_ThrowsArgumentExceptionListingAll()
    {
        var prompt = CreatePrompt(requiredVariables: ["a", "b", "c"]);

        var args = new Dictionary<string, object?> { ["a"] = "1" };

        var ex = Assert.Throws<ArgumentException>(() => prompt.ValidateRequired(args));
        Assert.Contains("b", ex.Message, StringComparison.Ordinal);
        Assert.Contains("c", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRequired_NoRequiredVariables_DoesNotThrow()
    {
        var prompt = CreatePrompt(requiredVariables: null);

        var ex = Record.Exception(() => prompt.ValidateRequired(new Dictionary<string, object?>()));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateRequired_SupersetArguments_DoesNotThrow()
    {
        var prompt = CreatePrompt(requiredVariables: ["name"]);

        var args = new Dictionary<string, object?> { ["name"] = "x", ["extra"] = "y" };

        var ex = Record.Exception(() => prompt.ValidateRequired(args));
        Assert.Null(ex);
    }
}