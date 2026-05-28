namespace InfraGate.AgentLlm.Tests.UnitTests;

public sealed class RateLimitRetryingChatClientTests
{
    private static readonly ChatResponse SuccessResult =
        new(new ChatMessage(ChatRole.Assistant, "ok"));

    private static ClientResultException Make429()
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(429);
        return new ClientResultException(response);
    }

    private static ClientResultException Make500()
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(500);
        return new ClientResultException(response);
    }

    [Fact]
    public async Task GetResponseAsync_SuccessOnFirstAttempt_ReturnsWithoutRetry()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessResult));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResponseAsync_429Once_RetriesOnceAndReturns()
    {
        var ex = Make429();
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ChatResponse>(ex),
                Task.FromResult(SuccessResult));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero]);

        var result = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Same(SuccessResult, result);
        await inner.Received(2).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResponseAsync_429ExceedsMaxRetries_RethrowsClientResultException()
    {
        var ex1 = Make429();
        var ex2 = Make429();
        var ex3 = Make429();
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ChatResponse>(ex1),
                Task.FromException<ChatResponse>(ex2),
                Task.FromException<ChatResponse>(ex3));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        var thrown = await Assert.ThrowsAsync<ClientResultException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(429, thrown.Status);
        await inner.Received(3).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResponseAsync_Non429ClientResultException_PropagatesImmediately()
    {
        var ex = Make500();
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChatResponse>(ex));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        var thrown = await Assert.ThrowsAsync<ClientResultException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(500, thrown.Status);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResponseAsync_OtherException_PropagatesImmediately()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChatResponse>(new InvalidOperationException("boom")));

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.Zero, TimeSpan.Zero]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResponseAsync_CancelledDuringDelay_ThrowsOperationCancelledException()
    {
        var ex = Make429();
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChatResponse>(ex));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = new RateLimitRetryingChatClient(inner, [TimeSpan.FromSeconds(10)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: cts.Token));
    }
}
