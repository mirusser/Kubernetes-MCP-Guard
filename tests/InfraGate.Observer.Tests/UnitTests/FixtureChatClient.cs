using InfraGate.AgentLlm;

namespace InfraGate.Observer.Tests.UnitTests;

/// <summary>
/// Test double that implements both <see cref="IChatClient"/> and <see cref="IChatClientFactory"/>.
/// Pass it as the factory argument to <see cref="InfraGate.Observer.Cycle.ObservationCycleRunner"/>
/// so the same fixture is used by <see cref="InfraGate.AgentLlm.ToolCallingAgentFactory"/> inside the workflow.
/// </summary>
public sealed class FixtureChatClient : IChatClient, IChatClientFactory
{
    private readonly Func<IEnumerable<ChatMessage>, ChatResponse> responseFactory;

    public FixtureChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> responseFactory)
    {
        this.responseFactory = responseFactory;
    }

    public FixtureChatClient(string textResponse)
        : this(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, textResponse)))
    {
    }

    // IChatClientFactory — returns itself so the workflow reuses this fixture.
    public IChatClient Create() => this;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(responseFactory(messages));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void Dispose() { }

    object? IChatClient.GetService(Type serviceType, object? serviceKey) => null;
}
