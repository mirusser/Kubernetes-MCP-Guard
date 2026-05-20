namespace InfraGate.Approvals;

public sealed record class FreshnessPolicy(IReadOnlyList<FreshnessCheck> Checks)
{
    public static FreshnessPolicy Empty { get; } = new([]);
}
