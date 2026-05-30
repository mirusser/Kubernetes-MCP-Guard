using System.Net;
using InfraGate.AgentLlm;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class AnthropicChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_ReturnsExpectedText()
    {
        var httpClient = CreateMockHttpClient("""{"id":"msg-123","model":"claude-3","content":[{"type":"text","text":"Hello world"}],"usage":{"input_tokens":10,"output_tokens":20}}""");

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Equal("Hello world", response.Text);
        Assert.Equal("msg-123", response.ResponseId);
        Assert.Equal("claude-3", response.ModelId);
    }

    [Fact]
    public async Task GetResponseAsync_WithMultipleContentBlocks_ConcatenatesText()
    {
        var httpClient = CreateMockHttpClient("""{"id":"msg-1","model":"claude-3","content":[{"type":"text","text":"Part one."},{"type":"text","text":"Part two."}],"usage":{"input_tokens":5,"output_tokens":10}}""");

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Equal("Part one.Part two.", response.Text);
    }

    [Fact]
    public async Task GetResponseAsync_NullContentArray_ReturnsEmptyString()
    {
        var httpClient = CreateMockHttpClient("""{"id":"msg-1","model":"claude-3","content":null,"usage":{"input_tokens":5,"output_tokens":10}}""");

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Equal(string.Empty, response.Text);
        Assert.NotNull(response.ResponseId);
    }

    [Fact]
    public async Task GetResponseAsync_NullUsage_SetsNullUsageDetails()
    {
        var httpClient = CreateMockHttpClient("""{"id":"msg-1","model":"claude-3","content":[{"type":"text","text":"No usage"}],"usage":null}""");

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Null(response.Usage);
    }

    [Fact]
    public async Task GetResponseAsync_NullResponse_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient("null");

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]));
    }

    [Fact]
    public async Task GetResponseAsync_NullMessages_ThrowsArgumentNullException()
    {
        using var client = new AnthropicChatClient(new HttpClient(), "claude-3");
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.GetResponseAsync(null!));
    }

    [Fact]
    public void GetStreamingResponseAsync_ThrowsNotSupportedException()
    {
        using var client = new AnthropicChatClient(new HttpClient(), "claude-3");
        Assert.Throws<NotSupportedException>(() =>
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]));
    }

    [Fact]
    public void Dispose_CanBeCalledWithoutException()
    {
        var client = new AnthropicChatClient(new HttpClient(), "claude-3");
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void GetService_ReturnsNull()
    {
        using var client = new AnthropicChatClient(new HttpClient(), "claude-3");
        Assert.Null(((IChatClient)client).GetService(typeof(object), null));
    }

    [Fact]
    public async Task GetResponseAsync_HttpError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        using var client = new AnthropicChatClient(httpClient, "claude-3");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]));
    }

    private static HttpClient CreateMockHttpClient(string jsonResponse)
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        });
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
