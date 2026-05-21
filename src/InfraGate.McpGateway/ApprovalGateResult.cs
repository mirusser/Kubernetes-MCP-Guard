namespace InfraGate.McpGateway;

public sealed record class ApprovalGateResult(
    ApprovalGateStatus Status,
    string Message,
    string? ReasonCode = null,
    string? ApprovalUrl = null,
    string? ChallengeId = null,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public bool IsApproved => Status is ApprovalGateStatus.Approved;

    public static ApprovalGateResult Approved() =>
        new(ApprovalGateStatus.Approved, string.Empty);

    public static ApprovalGateResult RequiresApproval(
        string message,
        string? reasonCode = null,
        string? approvalUrl = null,
        string? challengeId = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new(ApprovalGateStatus.ApprovalRequired, message, reasonCode, approvalUrl, challengeId, expiresAtUtc);

    public static ApprovalGateResult Refused(string message, string? reasonCode = null) =>
        new(ApprovalGateStatus.Refused, message, reasonCode);
}
