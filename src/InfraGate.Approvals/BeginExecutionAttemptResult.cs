namespace InfraGate.Approvals;

public sealed record class BeginExecutionAttemptResult(
    bool IsStarted,
    ExecutionAttempt? Attempt,
    string Message,
    string? ReasonCode = null)
{
    public static BeginExecutionAttemptResult Started(ExecutionAttempt attempt) =>
        new(true, attempt, "Execution attempt started.");

    public static BeginExecutionAttemptResult Refused(string message, string reasonCode) =>
        new(false, null, message, reasonCode);
}
