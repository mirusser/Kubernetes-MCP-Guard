namespace InfraGate.Remediation.Contracts;

public sealed record class RemediationProposalBatch
{
    public required string CycleId { get; init; }
    public required DateTimeOffset EmittedAt { get; init; }
    public required IReadOnlyList<RemediationProposal> Proposals { get; init; }
}
