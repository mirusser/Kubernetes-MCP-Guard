using InfraGate.Approvals;
using InfraGate.Approvals.AccessCodes;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalAccessCodeGeneratorTests
{
    [Fact]
    public void Generate_ReturnsCorrectLength()
    {
        string code = ApprovalAccessCodeGenerator.Generate();

        Assert.Equal(ApprovalConventions.AccessCodes.CodeLength, code.Length);
    }

    [Fact]
    public void Generate_AllCharsFromAlphabet()
    {
        for (int i = 0; i < 20; i++)
        {
            string code = ApprovalAccessCodeGenerator.Generate();

            Assert.All(code, c =>
                Assert.Contains(c, ApprovalConventions.AccessCodes.Alphabet));
        }
    }

    [Fact]
    public void Generate_DoesNotContainAmbiguousChars()
    {
        for (int i = 0; i < 50; i++)
        {
            string code = ApprovalAccessCodeGenerator.Generate();

            Assert.DoesNotContain(code, c => "0OIL1".Contains(c, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Generate_MultipleCalls_ProducesDistinctCodes()
    {
        var codes = Enumerable.Range(0, 50)
            .Select(_ => ApprovalAccessCodeGenerator.Generate())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(codes.Count > 1, "All 50 generated codes were identical — generator appears broken.");
    }

    [Fact]
    public void Generate_IsUppercase()
    {
        for (int i = 0; i < 20; i++)
        {
            string code = ApprovalAccessCodeGenerator.Generate();

            Assert.Equal(code, code.ToUpperInvariant(), StringComparer.Ordinal);
        }
    }
}
