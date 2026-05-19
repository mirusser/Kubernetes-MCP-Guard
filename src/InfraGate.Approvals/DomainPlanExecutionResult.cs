namespace InfraGate.Approvals;

public sealed record DomainPlanExecutionResult(bool IsSuccessful, string Message, string? TargetNamespace)
{
    public PlanAudit? Audit { get; init; }

    public static DomainPlanExecutionResult Success(string message, string? targetNamespace) =>
        new(true, message, targetNamespace);

    public static DomainPlanExecutionResult Blocked(string message) =>
        new(false, message, TargetNamespace: null);

    public static DomainPlanExecutionResult Blocked(string message, PlanAudit audit) =>
        new(false, message, TargetNamespace: null) { Audit = audit };
}
