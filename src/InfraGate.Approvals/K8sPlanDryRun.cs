namespace InfraGate.Approvals;

public sealed record K8sPlanDryRun(
    string Status,
    DateTimeOffset CheckedAtUtc,
    K8sPlanDryRunObject[] Objects,
    string[] Warnings,
    string Message);
