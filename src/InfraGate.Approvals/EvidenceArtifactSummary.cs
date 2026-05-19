namespace InfraGate.Approvals;

public sealed record EvidenceArtifactSummary(
    string Type,
    ApprovalDigest Digest,
    string? Reference,
    Dictionary<string, string> RedactionMetadata);
