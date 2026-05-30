namespace InfraGate.Approvals.Execution;

public interface IToolCaller
{
    Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}
