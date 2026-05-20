namespace InfraGate.Approvals;

public sealed record class EvidenceArtifactSummary(
    string Type,
    ApprovalDigest Digest,
    string? Reference,
    Dictionary<string, string> RedactionMetadata);
