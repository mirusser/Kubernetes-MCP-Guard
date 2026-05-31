using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.PreExecution;

namespace InfraGate.Approvals.Tests.UnitTests;

public sealed class PreExecutionGateResultTests
{
    [Fact]
    public void Blocked_NullAuditOnDomainResult_CreatesDefaultAuditEntry()
    {
        var domainResult = new DomainPlanExecutionResult(false, "Test reason", TargetNamespace: null);

        var result = PreExecutionGateResult.Blocked(domainResult, "plan-abc");

        Assert.False(result.IsPassed);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.ApplyDenied, result.Audit.EventName);
        Assert.Equal("plan-abc", result.Audit.PlanId);
    }

    [Fact]
    public void Blocked_ExistingAuditOnDomainResult_ReturnsProvidedAudit()
    {
        var audit = new ApprovalAuditEntry(
            ApprovalConventions.AuditEvents.ApplyDenied,
            new ApplyDeniedPayload("plan-abc", "detail"),
            PlanId: "plan-abc");
        var domainResult = new DomainPlanExecutionResult(false, "Test reason", TargetNamespace: null)
        {
            Audit = audit
        };

        var result = PreExecutionGateResult.Blocked(domainResult, "plan-abc");

        Assert.False(result.IsPassed);
        Assert.Same(audit, result.Audit);
    }
}
