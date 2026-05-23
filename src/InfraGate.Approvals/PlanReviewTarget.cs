namespace InfraGate.Approvals;

public sealed record class PlanReviewTarget(
    string Type,
    string Name,
    string? Scope,
    IReadOnlyDictionary<string, string> Attributes);
