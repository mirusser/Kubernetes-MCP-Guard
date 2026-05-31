using System.Text.Json;
using InfraGate.Approvals;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal static class ToolDefinitionFactory
{
    private const string JsonSchemaIntegerType = "integer";
    private const string JsonSchemaObjectType = "object";
    private const string JsonSchemaStringType = "string";

    private static readonly string[] ApplyApprovedPlanRequiredArgs = ["planId"];

    private static readonly string[] GetPlanStatusRequiredArgs = [McpGatewayConventions.ToolArguments.PlanId];

    private static readonly string[] WaitForPlanApprovalRequiredArgs = [McpGatewayConventions.ToolArguments.PlanId];

    private static readonly string[] ProposePlanRequiredArgs =
    [
        McpGatewayConventions.ToolArguments.OperationType,
        McpGatewayConventions.ToolArguments.OperationArguments
    ];

    internal static Tool CreateForwardedTool(DownstreamTool dt)
    {
        return new Tool
        {
            Name = dt.Name,
            Description = dt.Description,
            InputSchema = dt.InputSchema,
            Annotations = dt.IsReadOnly ? new ToolAnnotations { ReadOnlyHint = true } : null,
        };
    }

    internal static Tool CreateApplyApprovedPlanTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            Description = "Returns a browser approval URL for a pending plan, or applies it after approval. " +
                          "When this returns ApprovalRequired, you MUST call wait_for_plan_approval(planId=...) to poll for approval — do NOT wait for user confirmation. " +
                          "Repeat until Approved, then call this tool again to apply the plan.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = JsonSchemaObjectType,
                properties = new
                {
                    planId = new { type = JsonSchemaStringType, description = "PlanId returned by one of the request_* tools." }
                },
                required = ApplyApprovedPlanRequiredArgs
            })
        };
    }

    internal static Tool CreateGetPlanStatusTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.GetPlanStatus,
            Description = "Returns the current status of a pending approval plan (" +
                          ApprovalConventions.PlanStatusValues.NotFound + " | " +
                          ApprovalConventions.PlanStatusValues.ApprovalRequired + " | " +
                          ApprovalConventions.PlanStatusValues.Approved + " | " +
                          ApprovalConventions.PlanStatusValues.Applied + " | " +
                          ApprovalConventions.PlanStatusValues.Expired + "). " +
                          "Call this in a polling loop after " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " returns ApprovalRequired. When status is " +
                          ApprovalConventions.PlanStatusValues.Approved + ", call " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " to apply the plan. When status is " +
                          ApprovalConventions.PlanStatusValues.Expired + ", call " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " to create a new approval challenge.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = JsonSchemaObjectType,
                properties = new
                {
                    planId = new { type = JsonSchemaStringType, description = "PlanId returned by one of the request_* tools." }
                },
                required = GetPlanStatusRequiredArgs
            })
        };
    }

    internal static Tool CreateWaitForPlanApprovalTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.WaitForPlanApproval,
            Description = "Waits briefly for an approval plan to become approved, applied, expired, or missing without applying the plan.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = JsonSchemaObjectType,
                properties = new
                {
                    planId = new { type = JsonSchemaStringType, description = "PlanId returned by one of the request_* tools." },
                    timeoutSeconds = new
                    {
                        type = JsonSchemaIntegerType,
                        description = "How long to wait before returning ApprovalRequired with timedOut=true.",
                        minimum = 1,
                        maximum = 300,
                        @default = 55
                    }
                },
                required = WaitForPlanApprovalRequiredArgs
            })
        };
    }

    internal static Tool CreateProposePlanTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.ProposePlan,
            Description = "Creates an operator-approved remediation plan and sends an approval access code.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = JsonSchemaObjectType,
                properties = new
                {
                    operationType = new
                    {
                        type = JsonSchemaStringType,
                        description = "Allowed values: restart_deployment, scale_deployment."
                    },
                    arguments = new
                    {
                        type = JsonSchemaObjectType,
                        description = "Operation-specific arguments for the selected remediation operation."
                    }
                },
                required = ProposePlanRequiredArgs
            })
        };
    }
}
