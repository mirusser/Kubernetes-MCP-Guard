namespace InfraGate.Approvals;

public sealed record FreshnessPolicy(IReadOnlyList<FreshnessCheck> Checks)
{
    public static FreshnessPolicy Empty { get; } = new([]);
}
