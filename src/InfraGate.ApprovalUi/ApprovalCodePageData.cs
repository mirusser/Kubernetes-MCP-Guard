namespace InfraGate.ApprovalUi;

public sealed record class ApprovalCodePageData(
    string ActionUrl,
    string CodeFieldName,
    string AntiforgeryFieldName,
    string? AntiforgeryToken,
    string? SubmittedCode,
    string? Error);
