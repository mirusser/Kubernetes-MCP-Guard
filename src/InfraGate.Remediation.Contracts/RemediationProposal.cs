namespace InfraGate.Remediation.Contracts;

public sealed record class RemediationProposal
{
    public required string PlanId { get; init; }
    public required string AnomalyId { get; init; }
    public required DateTimeOffset ProposedAt { get; init; }
}
