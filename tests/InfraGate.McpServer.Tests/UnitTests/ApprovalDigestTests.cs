using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class ApprovalDigestTests
{
    private const string TestCanonicalization = "test-canonicalization-v1";

    [Fact]
    public void ComputeSha256_ObjectsWithDifferentPropertyOrder_Matches()
    {
        var left = new
        {
            zeta = "last",
            alpha = "first"
        };
        var right = new
        {
            alpha = "first",
            zeta = "last"
        };

        var leftDigest = ApprovalDigest.ComputeSha256(TestCanonicalization, left);
        var rightDigest = ApprovalDigest.ComputeSha256(TestCanonicalization, right);

        Assert.Equal(leftDigest, rightDigest);
    }

    [Fact]
    public void ComputeSha256_DictionariesWithDifferentInsertionOrder_Matches()
    {
        var left = new Dictionary<string, object?>
        {
            ["zeta"] = new Dictionary<string, object?>
            {
                ["nestedB"] = 2,
                ["nestedA"] = 1
            },
            ["alpha"] = "first"
        };
        var right = new Dictionary<string, object?>
        {
            ["alpha"] = "first",
            ["zeta"] = new Dictionary<string, object?>
            {
                ["nestedA"] = 1,
                ["nestedB"] = 2
            }
        };

        var leftDigest = ApprovalDigest.ComputeSha256(TestCanonicalization, left);
        var rightDigest = ApprovalDigest.ComputeSha256(TestCanonicalization, right);

        Assert.Equal(leftDigest, rightDigest);
    }

    [Fact]
    public void ComputeSha256_Value_DeclaresAlgorithmAndCanonicalization()
    {
        var digest = ApprovalDigest.ComputeSha256(
            TestCanonicalization,
            new { planId = "plan-1" });

        Assert.Equal(ApprovalConventions.Digests.Sha256, digest.Algorithm);
        Assert.Equal(TestCanonicalization, digest.Canonicalization);
        Assert.Matches("^[0-9A-Fa-f]{64}$", digest.Value);
    }
}
