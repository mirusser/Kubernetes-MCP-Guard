using InfraGate.AuditOutbox;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

// Advisory lock conventions are verified here.
// StreamLockKey and Streams constant stability are already covered by CanonicalizationTests.
// Lock serialization under contention is covered by LockContentionTests.
public sealed class AdvisoryLockConventionsTests
{
    [Fact]
    public void LockCategory_IsNonZero()
    {
        Assert.NotEqual(0, AuditOutboxConventions.LockCategory);
    }

    [Fact]
    public void LockCategory_DiffersFromStreamKey()
    {
        int approvalKey = AuditOutboxConventions.StreamLockKey("approvals");
        Assert.NotEqual(AuditOutboxConventions.LockCategory, approvalKey);
    }
}
