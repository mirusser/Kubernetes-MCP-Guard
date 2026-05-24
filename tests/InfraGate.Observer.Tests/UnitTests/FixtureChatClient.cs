using Microsoft.Extensions.AI;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class FixtureChatClient : IChatClient
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

    public void Dispose()
    {
    }

    object? IChatClient.GetService(Type serviceType, object? serviceKey)
    {
        return null;
    }
}
