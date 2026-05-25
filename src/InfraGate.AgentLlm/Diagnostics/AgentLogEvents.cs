namespace InfraGate.AgentLlm.Diagnostics;

internal static partial class AgentLogEvents
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "LLM response token usage: Input={InputTokens} Output={OutputTokens}")]
    internal static partial void LogLlmTokenUsage(ILogger logger, int inputTokens, int outputTokens);
}
