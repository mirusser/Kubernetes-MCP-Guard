namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class AllowAllModelVisibleContentGuardTests
{
    private static readonly ModelVisibleContent SampleContent = new(
        "snapshot json here",
        ModelVisibleContentSource.ObserverSnapshot,
        "observer-ns1",
        CorrelationId: "corr-1");

    [Fact]
    public async Task EvaluateAsync_AnyContent_ReturnsAllow()
    {
        var sut = AllowAllModelVisibleContentGuard.Instance;

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Allow, decision.Action);
    }

    [Fact]
    public async Task EvaluateAsync_AnyContent_ReturnsOriginalText()
    {
        var sut = AllowAllModelVisibleContentGuard.Instance;

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(SampleContent.Text, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_AnyContent_ReturnsEmptyCategories()
    {
        var sut = AllowAllModelVisibleContentGuard.Instance;

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Empty(decision.Categories);
    }

    [Fact]
    public async Task EvaluateAsync_AnyContent_ReturnsNoneReason()
    {
        var sut = AllowAllModelVisibleContentGuard.Instance;

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(AgentGuardrailConventions.Reasons.None, decision.Reason);
    }

    [Fact]
    public void Instance_IsSingleton_ReturnsSameReference()
    {
        Assert.Same(AllowAllModelVisibleContentGuard.Instance, AllowAllModelVisibleContentGuard.Instance);
    }
}
