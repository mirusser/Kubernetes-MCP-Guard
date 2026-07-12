namespace InfraGate.McpGateway;

internal static class ServerInstructions
{
    internal static readonly string ApprovalWorkflow =
        $"""
        Approval workflow (MANDATORY — no exceptions):
        1. After calling any {McpGatewayConventions.ToolNames.RequestToolPrefix}* tool, call {McpGatewayConventions.ToolNames.ApplyApprovedPlan}({McpGatewayConventions.ToolArguments.PlanId}=...) to get the approval URL.
        2. You MUST then call {McpGatewayConventions.ToolNames.WaitForPlanApproval}({McpGatewayConventions.ToolArguments.PlanId}=...) in a polling loop (55 s timeout, repeat as needed).
            Do NOT wait for the user to confirm approval — poll automatically.
        3. When {McpGatewayConventions.ToolNames.WaitForPlanApproval} returns Approved, call {McpGatewayConventions.ToolNames.ApplyApprovedPlan} again to apply the plan.
        Skipping the polling step and waiting for user confirmation instead is not permitted.
        """;
}
