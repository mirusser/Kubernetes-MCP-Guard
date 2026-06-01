using System.ClientModel;
using InfraGate.AgentLlm.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.AgentLlm;

public sealed class RateLimitRetryingChatClient(
    IChatClient inner,
    TimeSpan[] retryDelays,
    ILogger<RateLimitRetryingChatClient>? logger = null) : IChatClient
{
    private static readonly TimeSpan[] defaultRetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60),
    ];

    private readonly ILogger log = logger ?? NullLogger<RateLimitRetryingChatClient>.Instance;

    public RateLimitRetryingChatClient(IChatClient inner, ILogger<RateLimitRetryingChatClient>? logger = null)
        : this(inner, defaultRetryDelays, logger) { }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (log.IsEnabled(LogLevel.Information))
        {
            LogRaw(log, "llm.input ──────────────────────────\n" + FormatMessages(messages));
        }

        LogRaw(log, "llm.call ─────────────────────────── [awaiting response]");

        var totalDelay = TimeSpan.Zero;
        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            try
            {
                var response = await inner.GetResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false);
                if (attempt > 0)
                {
                    AgentLogEvents.LogRateLimitRecovered(log, attempt, retryDelays.Length, totalDelay.TotalSeconds);
                }
                if (log.IsEnabled(LogLevel.Information))
                {
                    LogRaw(log, "llm.output ─────────────────────────\n" + (response.Text ?? string.Empty));
                }
                return response;
            }
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                if (attempt >= retryDelays.Length)
                {
                    AgentLogEvents.LogRateLimitExhausted(log, retryDelays.Length);
                    throw;
                }
                var delay = retryDelays[attempt];
                totalDelay += delay;
                AgentLogEvents.LogRateLimitRetry(log, delay.TotalSeconds, attempt + 1, retryDelays.Length);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unreachable");
    }

    private static void LogRaw(ILogger logger, string text) =>
        logger.Log(LogLevel.Information, 0, text, null, static (s, _) => s);

    private static string FormatMessages(IEnumerable<ChatMessage> messages)
    {
        var parts = new List<string>();
        var i = 0;
        foreach (var msg in messages)
        {
            var header = $"── [{i++}:{msg.Role}] " + new string('─', 48);
            parts.Add($"{header}\n{msg.Text ?? "(empty)"}");
        }
        return string.Join("\n\n", parts);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();
}
