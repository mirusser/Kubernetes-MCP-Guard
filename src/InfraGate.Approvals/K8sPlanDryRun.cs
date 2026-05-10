namespace InfraGate.Approvals;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sPlanDryRun(
    string Status,
    DateTimeOffset CheckedAtUtc,
    K8sPlanDryRunObject[] Objects,
    string[] Warnings,
    string Message);
