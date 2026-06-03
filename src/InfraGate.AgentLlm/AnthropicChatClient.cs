using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using InfraGate.AgentLlm.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.AgentLlm;

// Custom IChatClient implementation: no official Microsoft.Extensions.AI.Anthropic package exists.
public sealed class AnthropicChatClient(
    HttpClient httpClient,
    string model,
    ILoggerFactory loggerFactory,
    Counter<long>? llmTokensCounter = null) : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<AnthropicChatClient> logger = loggerFactory.CreateLogger<AnthropicChatClient>();

    public AnthropicChatClient(HttpClient httpClient, string model)
        : this(httpClient, model, NullLoggerFactory.Instance) { }

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
            .Where(static c => c is { Type: "text", Text: not null })
            .Select(static c => c.Text!)
            .ToList();

        var responseText = textBlocks is { Count: > 0 }
            ? string.Concat(textBlocks)
            : string.Empty;

        if (string.Equals(anthropicResponse.StopReason, "max_tokens", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "LLM response was truncated at max_tokens limit ({MaxTokens}). Consider increasing MaxOutputTokens.",
                options?.MaxOutputTokens ?? 16384);
        }

        AgentLogEvents.LogLlmTokenUsage(logger,
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
                .Select(static tc => tc.Text));

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

        int maxTokens = options?.MaxOutputTokens ?? 16384;

        return new AnthropicRequestBody
        {
            Model = model,
            MaxTokens = maxTokens,
            System = systemPrompt,
            Messages = apiMessages,
        };
    }

    // JSON serialization/deserialization DTOs; properties set by System.Text.Json at runtime.
#pragma warning disable S1144, S3459, CA1812
    private sealed record class AnthropicRequestBody
    {
        public string? Model { get; set; }
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
        public string? System { get; set; }
        public List<AnthropicApiMessage>? Messages { get; set; }
    }

    private sealed record class AnthropicApiMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    private sealed record class AnthropicResponseBody
    {
        public string? Id { get; set; }
        public string? Model { get; set; }
        public List<AnthropicContentBlock>? Content { get; set; }
        public AnthropicUsageInfo? Usage { get; set; }
        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
    }

    private sealed record class AnthropicContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    private sealed record class AnthropicUsageInfo
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
#pragma warning restore S1144, S3459, CA1812
}
