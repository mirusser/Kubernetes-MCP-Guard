namespace InfraGate.McpGateway.Email;

public sealed record class ApprovalEmailTemplateData(
    string PlanId,
    string PlanSummary,
    string AccessCode,
    string ApprovalUrl,
    DateTimeOffset ExpiresAtUtc);
