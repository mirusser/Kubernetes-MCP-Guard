using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.AgentLlm.Tests.UnitTests;

public sealed class RateLimitRetryingChatClientTests
{
    private static readonly ChatResponse SuccessResult =
        new(new ChatMessage(ChatRole.Assistant, "ok"));

    private static ClientResultException Make429() =>
        new(new FakePipelineResponse(429));

    private static ClientResultException Make500() =>
        new(new FakePipelineResponse(500));

    [Fact]
    public async Task GetResponseAsync_SuccessOnFirstAttempt_ReturnsWithoutRetry()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromResult(SuccessResult));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_429Once_RetriesOnceAndReturns()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));
        inner.Enqueue(Task.FromResult(SuccessResult));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_429ExceedsMaxRetries_RethrowsClientResultException()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        var thrown = await Assert.ThrowsAsync<ClientResultException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(429, thrown.Status);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_Non429ClientResultException_PropagatesImmediately()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(Make500()));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        var thrown = await Assert.ThrowsAsync<ClientResultException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(500, thrown.Status);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_OtherException_PropagatesImmediately()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(new InvalidOperationException("boom")));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_CancelledDuringDelay_ThrowsOperationCancelledException()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.FromSeconds(10)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetResponseAsync_WithRealLogger_DoesNotThrow()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromResult(SuccessResult));

        var logger = NullLogger<RateLimitRetryingChatClient>.Instance;
        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero], logger);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
    }

    [Fact]
    public async Task GetResponseAsync_ResponseWithNullText_ReturnsNullText()
    {
        var responseWithNullText = new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null));
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromResult(responseWithNullText));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(responseWithNullText, result);
    }

    [Fact]
    public async Task GetResponseAsync_429WithRealLogger_RetriesAndLogs()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromException<ChatResponse>(Make429()));
        inner.Enqueue(Task.FromResult(SuccessResult));

        var logger = NullLogger<RateLimitRetryingChatClient>.Instance;
        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero], logger);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_MessageWithNullText_DoesNotThrow()
    {
        var inner = new FakeChatClient();
        inner.Enqueue(Task.FromResult(SuccessResult));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync(
            [new ChatMessage(ChatRole.User, (string?)null)], cancellationToken: CancellationToken.None);

        Assert.Same(SuccessResult, result);
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed class FakePipelineResponse(int statusCode) : PipelineResponse
    {
        public override int Status => statusCode;
        public override string ReasonPhrase => $"{statusCode}";
        public override Stream? ContentStream { get => null; set { } }
        public override BinaryData Content => BinaryData.Empty;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override void Dispose() { }
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<Task<ChatResponse>> responses = new();
        public int CallCount { get; private set; }

        public void Enqueue(Task<ChatResponse> response) => responses.Enqueue(response);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return responses.TryDequeue(out var next)
                ? next
                : throw new InvalidOperationException("No more enqueued responses.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
