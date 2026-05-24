using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Llm;

// Custom IChatClient implementation: no official Microsoft.Extensions.AI.Anthropic NuGet package exists.
internal sealed class AnthropicChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly string model;
    private readonly ILogger<AnthropicChatClient> logger;
    private readonly Counter<long>? llmTokensCounter;

    public AnthropicChatClient(HttpClient httpClient, string model)
        : this(httpClient, model, NullLoggerFactory.Instance, null)
    {
    }

    public AnthropicChatClient(HttpClient httpClient, string model, ILoggerFactory loggerFactory, Meter? meter = null)
    {
        this.httpClient = httpClient;
        this.model = model;
        logger = loggerFactory.CreateLogger<AnthropicChatClient>();
        llmTokensCounter = ObserverMetrics.CreateLlmTokensCounter(meter);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var messagesList = messages.ToList();
        var requestBody = BuildRequestBody(messagesList, options);
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpResponse = await httpClient.PostAsync(
            "/v1/messages", content, cancellationToken).ConfigureAwait(false);

        httpResponse.EnsureSuccessStatusCode();

        var responseStream = await httpResponse.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var anthropicResponse = await JsonSerializer.DeserializeAsync<AnthropicResponseBody>(
            responseStream, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (anthropicResponse is null)
        {
            throw new InvalidOperationException("Failed to deserialize Anthropic API response.");
        }

        var textBlocks = anthropicResponse.Content?
            .Where(c => c is { Type: "text", Text: not null })
            .Select(c => c.Text!)
            .ToList();

        var responseText = textBlocks is { Count: > 0 }
            ? string.Concat(textBlocks)
            : string.Empty;

        ObserverLogEvents.LogLlmTokenUsage(logger,
            anthropicResponse.Usage?.InputTokens ?? 0,
            anthropicResponse.Usage?.OutputTokens ?? 0);

        if (llmTokensCounter is not null && anthropicResponse.Usage is not null)
        {
            llmTokensCounter.Add(anthropicResponse.Usage.InputTokens + anthropicResponse.Usage.OutputTokens);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
        {
            ResponseId = anthropicResponse.Id,
            ModelId = anthropicResponse.Model,
            Usage = anthropicResponse.Usage is not null
                ? new UsageDetails
                {
                    InputTokenCount = anthropicResponse.Usage.InputTokens,
                    OutputTokenCount = anthropicResponse.Usage.OutputTokens,
                }
                : null,
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Streaming is not supported for the Anthropic chat client.");
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    object? IChatClient.GetService(Type serviceType, object? serviceKey)
    {
        return null;
    }

    private AnthropicRequestBody BuildRequestBody(List<ChatMessage> messages, ChatOptions? options)
    {
        string? systemPrompt = null;
        var apiMessages = new List<AnthropicApiMessage>();

        foreach (var msg in messages)
        {
            var textContent = string.Concat(msg.Contents
                .OfType<TextContent>()
                .Select(tc => tc.Text));

            if (msg.Role == ChatRole.System)
            {
                systemPrompt = systemPrompt is null
                    ? textContent
                    : throw new InvalidOperationException("Only one system message is supported.");
            }
            else
            {
                var role = msg.Role == ChatRole.Assistant ? "assistant" : "user";
                apiMessages.Add(new AnthropicApiMessage { Role = role, Content = textContent });
            }
        }

        var maxTokens = options?.MaxOutputTokens ?? 4096;

        return new AnthropicRequestBody
        {
            Model = model,
            MaxTokens = maxTokens,
            System = systemPrompt,
            Messages = apiMessages,
        };
    }

    // ── Anthropic API DTOs ──────────────────────────────────

    private sealed class AnthropicRequestBody
    {
        public string? Model { get; set; }
        public int MaxTokens { get; set; }
        public string? System { get; set; }
        public List<AnthropicApiMessage>? Messages { get; set; }
    }

    private sealed class AnthropicApiMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    private sealed class AnthropicResponseBody
    {
        public string? Id { get; set; }
        public string? Model { get; set; }
        public List<AnthropicContentBlock>? Content { get; set; }
        public AnthropicUsageInfo? Usage { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    private sealed class AnthropicUsageInfo
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
