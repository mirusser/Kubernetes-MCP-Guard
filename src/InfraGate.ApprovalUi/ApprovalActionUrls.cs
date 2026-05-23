namespace InfraGate.ApprovalUi;

public sealed record class ApprovalActionUrls(
    string ApproveUrl,
    string DenyUrl,
    string CancelUrl,
    string AntiforgeryFieldName,
    string? AntiforgeryToken);
