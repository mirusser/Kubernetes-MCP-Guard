namespace InfraGate.AgentMcp.Tests.IntegrationTests;

public sealed class AgentMcpToolsetTests
{
    [Fact]
    public async Task GetAgentToolsAsync_WhenConnected_ReturnsOnlyProfiledDiagnosticTools()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var tools = await toolset.GetAgentToolsAsync(CancellationToken.None);

        var names = tools.OfType<AIFunction>().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, tools.Count);
        Assert.Contains(InProcessMcpServerFixture.ReadOnlyToolName, names);
        Assert.Contains(InProcessMcpServerFixture.SecondaryReadOnlyToolName, names);
    }

    [Fact]
    public async Task GetAgentToolsAsync_WhenConnected_ExcludesNonReadOnlyTools()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var tools = await toolset.GetAgentToolsAsync(CancellationToken.None);

        var names = tools.OfType<AIFunction>().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(InProcessMcpServerFixture.MutationToolName, names);
    }

    [Fact]
    public async Task GetAgentToolsAsync_WhenConnected_ExcludesReadOnlyToolsNotInProfile()
    {
        // ReadOnlyHint=true alone must not be sufficient authority: this tool is read-only but
        // its name is not one of the reviewed diagnostic reads.
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var tools = await toolset.GetAgentToolsAsync(CancellationToken.None);

        var names = tools.OfType<AIFunction>().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(InProcessMcpServerFixture.UnprofiledReadOnlyToolName, names);
    }

    [Fact]
    public async Task GetAgentToolsAsync_WhenConnected_ExcludesSchemaDriftedTools()
    {
        // Same profiled name, but a declared schema that no longer matches the pinned property
        // set must still be excluded rather than trusted.
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var tools = await toolset.GetAgentToolsAsync(CancellationToken.None);

        var names = tools.OfType<AIFunction>().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(InProcessMcpServerFixture.SchemaDriftedToolName, names);
    }

    [Fact]
    public async Task CallToolAsync_WhenReadOnlyTool_ReturnsRawCallToolResult()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var result = await toolset.CallToolAsync(
            InProcessMcpServerFixture.ReadOnlyToolName,
            null,
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        string text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal(InProcessMcpServerFixture.ReadOnlyToolResponse, text);
    }

    [Fact]
    public async Task CallToolAsync_WhenMutationTool_ReturnsRawCallToolResult()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        var result = await toolset.CallToolAsync(
            InProcessMcpServerFixture.MutationToolName,
            null,
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        string text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal(InProcessMcpServerFixture.MutationToolResponse, text);
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNotThrowOnSecondCall()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        Assert.True(toolset.IsConnected);

        var ex = await Record.ExceptionAsync(() => toolset.ConnectAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GetAgentToolsAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var options = new AgentMcpOptions { GatewayBaseUrl = "http://localhost:9999/mcp" };
        var toolset = new AgentMcpToolset(options, new StubTokenProvider(), NullLoggerFactory.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => toolset.GetAgentToolsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAgentToolsAsync_WhenConnected_TokenProviderCalled()
    {
        await using var fixture = InProcessMcpServerFixture.Create();
        await using var toolset = await fixture.CreateToolsetAsync();

        await toolset.GetAgentToolsAsync(CancellationToken.None);

        Assert.True(fixture.TokenProvider.GetTokenCalls > 0);
    }
}