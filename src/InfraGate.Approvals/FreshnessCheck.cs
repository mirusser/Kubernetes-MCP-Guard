namespace InfraGate.Approvals;

public sealed record FreshnessCheck(string Type, IReadOnlyDictionary<string, string> Parameters);
