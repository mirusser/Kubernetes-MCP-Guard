using InfraGate.Approvals;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class ApprovalConventionsTests
{
    [Fact]
    public void AuditEvents_TargetSpineValues_ArePinned()
    {
        Assert.Equal("plan.created", ApprovalConventions.AuditEvents.PlanRequested);
        Assert.Equal("execution.succeeded", ApprovalConventions.AuditEvents.PlanApplied);
        Assert.Equal("execution.blocked", ApprovalConventions.AuditEvents.ApplyDenied);
        Assert.Equal("execution.failed", ApprovalConventions.AuditEvents.ApplyFailed);
        Assert.Equal("execution.blocked", ApprovalConventions.AuditEvents.DryRunFailed);
        Assert.Equal("execution.blocked", ApprovalConventions.AuditEvents.DiffFailed);
        Assert.Equal("execution.blocked", ApprovalConventions.AuditEvents.ApplyDriftDetected);
        Assert.Equal("challenge.created", ApprovalConventions.AuditEvents.ApprovalChallengeCreated);
        Assert.Equal("challenge.approved", ApprovalConventions.AuditEvents.ApprovalChallengeApproved);
        Assert.Equal("challenge.denied", ApprovalConventions.AuditEvents.ApprovalChallengeDenied);
        Assert.Equal("challenge.expired", ApprovalConventions.AuditEvents.ApprovalChallengeExpired);
        Assert.Equal("challenge.rejected", ApprovalConventions.AuditEvents.ApprovalChallengeRejected);
        Assert.Equal("challenge.canceled", ApprovalConventions.AuditEvents.ApprovalChallengeCanceled);
        Assert.Equal("grant.issued", ApprovalConventions.AuditEvents.GrantIssued);
    }
}
