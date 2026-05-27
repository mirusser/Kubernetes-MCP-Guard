using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.McpGateway;

internal static class PlanStatusResponse
{
    public static string Serialize(string planId, PlanStatus status)
    {
        var response = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.PlanId] = planId,
            [McpGatewayConventions.ToolResponseFields.Status] = ToPlanStatusValue(status)
        };

        return JsonSerializer.Serialize(response);
    }

    public static string Serialize(string planId, PlanStatus status, bool timedOut)
    {
        var response = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.PlanId] = planId,
            [McpGatewayConventions.ToolResponseFields.Status] = ToPlanStatusValue(status),
            [McpGatewayConventions.ToolResponseFields.TimedOut] = timedOut
        };

        return JsonSerializer.Serialize(response);
    }

    public static string ToPlanStatusValue(PlanStatus status) =>
        status switch
        {
            PlanStatus.NotFound => ApprovalConventions.PlanStatusValues.NotFound,
            PlanStatus.ApprovalRequired => ApprovalConventions.PlanStatusValues.ApprovalRequired,
            PlanStatus.Approved => ApprovalConventions.PlanStatusValues.Approved,
            PlanStatus.Applied => ApprovalConventions.PlanStatusValues.Applied,
            PlanStatus.Expired => ApprovalConventions.PlanStatusValues.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
