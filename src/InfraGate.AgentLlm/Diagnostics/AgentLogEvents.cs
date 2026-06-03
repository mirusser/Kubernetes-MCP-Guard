namespace InfraGate.AgentLlm.Diagnostics;

internal static partial class AgentLogEvents
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "LLM response token usage: Input={InputTokens} Output={OutputTokens}")]
    internal static partial void LogLlmTokenUsage(ILogger logger, int inputTokens, int outputTokens);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "llm.rate_limited: provider returned 429 — waiting {DelaySeconds}s before retry {Attempt}/{MaxRetries}")]
    internal static partial void LogRateLimitRetry(ILogger logger, double delaySeconds, int attempt, int maxRetries);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "llm.rate_limited: all {MaxRetries} retries exhausted; re-throwing")]
    internal static partial void LogRateLimitExhausted(ILogger logger, int maxRetries);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "llm.rate_limited: recovered on attempt {Attempt}/{MaxRetries} after {TotalDelaySeconds}s total wait")]
    internal static partial void LogRateLimitRecovered(ILogger logger, int attempt, int maxRetries, double totalDelaySeconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "llm.pretty_print.parse_failed: message body is not valid JSON — logging raw text")]
    internal static partial void LogPrettyPrintParseFailed(ILogger logger, Exception ex);

}
