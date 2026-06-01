namespace InfraGate.Remediation.Contracts;

public sealed record class ExecutorDispatchResult
{
    public required string Status { get; init; }
    public required string Detail { get; init; }

    public static ExecutorDispatchResult Applied(string detail) =>
        new() { Status = ExecutorDispatchStatuses.Applied, Detail = detail };

    public static ExecutorDispatchResult Failed(string detail) =>
        new() { Status = ExecutorDispatchStatuses.Failed, Detail = detail };

    public static ExecutorDispatchResult Rejected(string detail) =>
        new() { Status = ExecutorDispatchStatuses.Rejected, Detail = detail };
}

public static class ExecutorDispatchStatuses
{
    public const string Applied = "applied";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
}
