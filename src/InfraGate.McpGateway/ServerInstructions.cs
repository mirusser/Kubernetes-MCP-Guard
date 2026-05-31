namespace InfraGate.McpGateway;

internal static class ServerInstructions
{
    internal const string ApprovalWorkflow =
        """
        Approval workflow (MANDATORY — no exceptions):
        1. After calling any request_* tool, call execute_approved_plan(planId=...) to get the approval URL.
        2. You MUST then call wait_for_plan_approval(planId=...) in a polling loop (55 s timeout, repeat as needed).
            Do NOT wait for the user to confirm approval — poll automatically.
        3. When wait_for_plan_approval returns Approved, call execute_approved_plan again to apply the plan.
        Skipping the polling step and waiting for user confirmation instead is not permitted.
        """;
}