namespace InfraGate.Prompts.Tests.UnitTests;

public sealed class PromptLibraryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInfraGatePromptLibrary_RegistersSingleton()
    {
        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(b => b.AddTemplate("t", "text"));

        using var provider = services.BuildServiceProvider();

        var a = provider.GetRequiredService<IPromptLibrary>();
        var b = provider.GetRequiredService<IPromptLibrary>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AddInfraGatePromptLibrary_NullConfigure_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddInfraGatePromptLibrary(null!));
    }

    [Fact]
    public async Task AddInfraGatePromptLibrary_RegisteredTemplate_RendersCorrectly()
    {
        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(b => b.AddTemplate("greet", "Hello {{name}}!", ["name"]));

        using var provider = services.BuildServiceProvider();
        var library = provider.GetRequiredService<IPromptLibrary>();

        var result = await library.RenderAsync("greet", new Dictionary<string, object?> { ["name"] = "World" });

        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void AddInfraGatePromptLibrary_ReturnsSameInstance_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddInfraGatePromptLibrary(b => b.AddTemplate("t", "text"));

        Assert.Same(services, result);
    }
}