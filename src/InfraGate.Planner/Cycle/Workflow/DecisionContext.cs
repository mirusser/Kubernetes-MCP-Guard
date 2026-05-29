using InfraGate.Planner.Decision;

namespace InfraGate.Planner.Cycle.Workflow;

internal sealed record class DecisionContext(AnomalyReport Report, RemediationDecision Decision);
