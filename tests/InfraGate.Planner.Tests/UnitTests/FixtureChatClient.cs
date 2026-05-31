using InfraGate.AgentLlm;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Tests.UnitTests;

internal sealed class FixtureChatClient : IChatClient, IChatClientFactory
{
    private readonly Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> responseFactory;

    public FixtureChatClient(string textResponse)
        : this(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, textResponse)))
    {
    }

    public FixtureChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> responseFactory)
        : this((messages, _) => Task.FromResult(responseFactory(messages)))
    {
    }

    public FixtureChatClient(Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> responseFactory)
    {
        this.responseFactory = responseFactory;
    }

    // IChatClientFactory — returns itself so the workflow reuses this fixture.
    public IChatClient Create() => this;

    /// <summary>The options from the most recent <see cref="GetResponseAsync"/> call.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return responseFactory(messages, cancellationToken);
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
