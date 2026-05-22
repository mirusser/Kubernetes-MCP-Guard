using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalCanonicalJsonTests
{
    [Fact]
    public void Serialize_ProducesConsistentOutput()
    {
        var value = new { foo = "bar", count = 42 };

        var first = ApprovalCanonicalJson.Serialize(value);
        var second = ApprovalCanonicalJson.Serialize(value);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_NullValue_ReturnsNullLiteral()
    {
        var result = ApprovalCanonicalJson.Serialize(null);

        Assert.Equal("null", result);
    }

    [Fact]
    public void ComputeSha256Hex_ProducesDeterministicHash()
    {
        const string input = "hello world";

        var hash1 = ApprovalCanonicalJson.ComputeSha256Hex(input);
        var hash2 = ApprovalCanonicalJson.ComputeSha256Hex(input);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hex_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = ApprovalCanonicalJson.ComputeSha256Hex("input-a");
        var hash2 = ApprovalCanonicalJson.ComputeSha256Hex("input-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hex_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ApprovalCanonicalJson.ComputeSha256Hex(null!));
    }

    [Fact]
    public void ComputeSha256Hex_ReturnsUppercaseHexOf64Chars()
    {
        var hash = ApprovalCanonicalJson.ComputeSha256Hex("any input");

        Assert.Matches("^[0-9A-F]{64}$", hash);
    }

    [Fact]
    public void Serialize_ThenComputeSha256Hex_ProducesStableDigest()
    {
        var value = new { operation = "apply", target = "default/nginx" };
        var canonical = ApprovalCanonicalJson.Serialize(value);

        var hash1 = ApprovalCanonicalJson.ComputeSha256Hex(canonical);
        var hash2 = ApprovalCanonicalJson.ComputeSha256Hex(canonical);

        Assert.Equal(hash1, hash2);
        Assert.Matches("^[0-9A-F]{64}$", hash1);
    }
}
