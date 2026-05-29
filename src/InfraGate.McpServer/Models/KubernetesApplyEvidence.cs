namespace InfraGate.McpServer.Models;

public sealed record class KubernetesApplyEvidence(
    KubernetesPlanDryRun DryRun,
    KubernetesPlanPolicyFinding[] PolicyFindings,
    bool PolicyBlocked,
    string? PolicyRefusal);
