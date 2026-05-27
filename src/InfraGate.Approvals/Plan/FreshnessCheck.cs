namespace InfraGate.Approvals.Plan;

public sealed record class FreshnessCheck(string Type, IReadOnlyDictionary<string, string> Parameters);
