namespace InfraGate.McpGateway.Email;

public sealed record class ApprovalEmailContent(
    string ToAddress,
    string Subject,
    string BodyPlaintext);
