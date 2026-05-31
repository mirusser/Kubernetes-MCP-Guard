namespace InfraGate.Observer.Cycle.Workflow;

internal sealed record class NamespaceParseResult(
    string NamespaceName,
    string CycleId,
    IReadOnlyList<AnomalyReport> Reports,
    int ToolCallsUsed,
    int SeverityDisagreements);
