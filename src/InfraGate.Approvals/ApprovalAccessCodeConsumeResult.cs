namespace InfraGate.Approvals;

public sealed record class ApprovalAccessCodeConsumeResult(
    bool Succeeded,
    string? ChallengeId,
    string Message,
    string ReasonCode)
{
    public static ApprovalAccessCodeConsumeResult Success(string challengeId) =>
        new(true, challengeId, "Approval access code accepted.", string.Empty);

    public static ApprovalAccessCodeConsumeResult Invalid() =>
        new(false, null, "Approval code is invalid.", ApprovalConventions.AccessCodes.ResultReasonCodes.Invalid);

    public static ApprovalAccessCodeConsumeResult Expired() =>
        new(false, null, "Approval code has expired.", ApprovalConventions.AccessCodes.ResultReasonCodes.Expired);

    public static ApprovalAccessCodeConsumeResult Consumed() =>
        new(false, null, "Approval code has already been used.", ApprovalConventions.AccessCodes.ResultReasonCodes.Consumed);
}
