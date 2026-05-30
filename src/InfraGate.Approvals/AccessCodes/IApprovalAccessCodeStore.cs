namespace InfraGate.Approvals.AccessCodes;

public interface IApprovalAccessCodeStore
{
    Task<ApprovalAccessCode> GenerateAsync(
        string challengeId,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    Task<ApprovalAccessCodeConsumeResult> ConsumeAsync(
        string code,
        CancellationToken cancellationToken);
}
