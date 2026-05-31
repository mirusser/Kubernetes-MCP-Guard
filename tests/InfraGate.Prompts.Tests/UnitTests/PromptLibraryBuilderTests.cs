namespace InfraGate.Prompts.Tests.UnitTests;

public sealed class PromptLibraryBuilderTests
{
    [Fact]
    public void AddTemplate_ValidNameAndTemplate_ReturnsBuilderForChaining()
    {
        var builder = new PromptLibraryBuilder();

        var result = builder.AddTemplate("test", "Hello {{name}}.");

        Assert.Same(builder, result);
    }

    [Fact]
    public void AddTemplate_NullName_ThrowsArgumentNullException()
    {
        var builder = new PromptLibraryBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddTemplate(null!, "Hello {{name}}."));
    }

    [Fact]
    public void AddTemplate_EmptyName_ThrowsArgumentException()
    {
        var builder = new PromptLibraryBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.AddTemplate("", "Hello {{name}}."));
    }

    [Fact]
    public void AddTemplate_NullTemplateText_ThrowsArgumentNullException()
    {
        var builder = new PromptLibraryBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddTemplate("test", null!));
    }

    [Fact]
    public void AddTemplate_NoRequiredVariables_DefaultToEmptyList()
    {
        var builder = new PromptLibraryBuilder();
        builder.AddTemplate("no-vars", "static text");

        var templates = builder.Build();

        Assert.Single(templates);
    }

    [Fact]
    public void AddTemplate_DuplicateName_LastWins()
    {
        var builder = new PromptLibraryBuilder();
        builder.AddTemplate("t", "first: {{x}}", ["x"]);
        builder.AddTemplate("t", "second: {{y}}", ["y"]);

        var templates = builder.Build();

        Assert.Single(templates);
    }

    [Fact]
    public void Build_Empty_ReturnsEmptyDictionary()
    {
        var builder = new PromptLibraryBuilder();

        var result = builder.Build();

        Assert.Empty(result);
    }

    [Fact]
    public void Build_MultipleTemplates_ReturnsAll()
    {
        var builder = new PromptLibraryBuilder();
        builder.AddTemplate("a", "template A", ["x"]);
        builder.AddTemplate("b", "template B");

        var result = builder.Build();

        Assert.Equal(2, result.Count);
    }
}