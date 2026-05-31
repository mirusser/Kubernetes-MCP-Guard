using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.Approvals.Tests.UnitTests;

public sealed class ApprovalConventionsTests
{
    [Fact]
    public void AuditEvents_TargetSpineValues_ArePinned()
    {
        Assert.Equal("plan.created", ApprovalConventions.AuditEvents.PlanRequested);
        Assert.Equal("pre_execution.grant.validated", ApprovalConventions.AuditEvents.PreExecutionGrantValidated);
        Assert.Equal("pre_execution.checked", ApprovalConventions.AuditEvents.PreExecutionChecked);
        Assert.Equal("execution.started", ApprovalConventions.AuditEvents.ExecutionStarted);
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

    [Fact]
    public void ChallengeStatuses_ArePinned()
    {
        Assert.Equal("pending", ApprovalConventions.ChallengeStatuses.Pending);
        Assert.Equal("approved", ApprovalConventions.ChallengeStatuses.Approved);
        Assert.Equal("denied", ApprovalConventions.ChallengeStatuses.Denied);
        Assert.Equal("expired", ApprovalConventions.ChallengeStatuses.Expired);
        Assert.Equal("rejected", ApprovalConventions.ChallengeStatuses.Rejected);
        Assert.Equal("canceled", ApprovalConventions.ChallengeStatuses.Canceled);
    }

    [Fact]
    public void ExecutionOutcomeStatuses_ArePinned()
    {
        Assert.Equal("blocked", ApprovalConventions.ExecutionOutcomeStatuses.Blocked);
        Assert.Equal("failed", ApprovalConventions.ExecutionOutcomeStatuses.Failed);
        Assert.Equal("succeeded", ApprovalConventions.ExecutionOutcomeStatuses.Succeeded);
    }

    [Fact]
    public void ApprovalPolicyTypes_ArePinned()
    {
        Assert.Equal("same-subject", ApprovalConventions.ApprovalPolicyTypes.SameSubject);
        Assert.Equal("operator-approval", ApprovalConventions.ApprovalPolicyTypes.OperatorApproval);
    }

    [Fact]
    public void PlanStatusValues_ArePinned()
    {
        Assert.Equal("NotFound", ApprovalConventions.PlanStatusValues.NotFound);
        Assert.Equal("ApprovalRequired", ApprovalConventions.PlanStatusValues.ApprovalRequired);
        Assert.Equal("Approved", ApprovalConventions.PlanStatusValues.Approved);
        Assert.Equal("Applied", ApprovalConventions.PlanStatusValues.Applied);
        Assert.Equal("Expired", ApprovalConventions.PlanStatusValues.Expired);
    }

    [Fact]
    public void AccessCodes_AlphabetLength_Is31()
    {
        Assert.Equal(31, ApprovalConventions.AccessCodes.Alphabet.Length);
    }

    [Fact]
    public void AccessCodes_CodeLength_Is8()
    {
        Assert.Equal(8, ApprovalConventions.AccessCodes.CodeLength);
    }

    [Fact]
    public void AccessCodes_Alphabet_ExcludesAmbiguousChars()
    {
        string alphabet = ApprovalConventions.AccessCodes.Alphabet;
        Assert.DoesNotContain('0', alphabet);
        Assert.DoesNotContain('O', alphabet);
        Assert.DoesNotContain('I', alphabet);
        Assert.DoesNotContain('L', alphabet);
        Assert.DoesNotContain('1', alphabet);
    }

    [Fact]
    public void Storage_DirectoryNames_ArePinned()
    {
        Assert.Equal("pending", ApprovalConventions.Storage.PendingDirectory);
        Assert.Equal("applied", ApprovalConventions.Storage.AppliedDirectory);
        Assert.Equal("challenges", ApprovalConventions.Storage.ChallengesDirectory);
        Assert.Equal("grants", ApprovalConventions.Storage.GrantsDirectory);
        Assert.Equal(".json", ApprovalConventions.Storage.JsonExtension);
    }

    [Fact]
    public void PlanValidity_DefaultWindow_IsOneHour()
    {
        Assert.Equal(TimeSpan.FromHours(1), ApprovalConventions.PlanValidity.DefaultWindow);
    }

    [Fact]
    public void ResultReasonCodes_PlanNotApproved_IsPinned()
    {
        Assert.Equal("approval.plan.not_approved", ApprovalConventions.ResultReasonCodes.PlanNotApproved);
    }

    [Fact]
    public void ResultReasonCodes_ChallengeExpired_IsPinned()
    {
        Assert.Equal("approval.challenge.expired", ApprovalConventions.ResultReasonCodes.ChallengeExpired);
    }

    [Fact]
    public void DiffChangeTypes_ArePinned()
    {
        Assert.Equal("create", ApprovalConventions.DiffChangeTypes.Create);
        Assert.Equal("update", ApprovalConventions.DiffChangeTypes.Update);
        Assert.Equal("delete", ApprovalConventions.DiffChangeTypes.Delete);
        Assert.Equal("no-op", ApprovalConventions.DiffChangeTypes.NoOp);
    }

    [Fact]
    public void ExecutionReusePolicyTypes_SingleExecution_IsPinned()
    {
        Assert.Equal("single-execution", ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution);
    }
}
