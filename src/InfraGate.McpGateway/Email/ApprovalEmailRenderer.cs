namespace InfraGate.McpGateway.Email;

public static class ApprovalEmailRenderer
{
    public static string RenderPlaintext(ApprovalEmailTemplateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return string.Join('\n',
            "InfraGate approval requested.",
            $"Plan: {data.PlanId}",
            $"Summary: {data.PlanSummary}",
            $"Code: {data.AccessCode}",
            $"Review: {data.ApprovalUrl}",
            $"Expires: {data.ExpiresAtUtc:O}");
    }
}
