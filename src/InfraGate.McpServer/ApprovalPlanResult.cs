namespace InfraGate.McpServer;

public sealed record ApprovalPlanResult(K8sPlan Plan, string PendingPath, string ApprovedPath, string Hash);
