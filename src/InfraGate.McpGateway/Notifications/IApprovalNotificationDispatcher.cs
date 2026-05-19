namespace InfraGate.McpGateway.Notifications;

internal interface IApprovalNotificationDispatcher
{
    Task NotifyPlanApprovedAsync(string planId, CancellationToken ct);
}
