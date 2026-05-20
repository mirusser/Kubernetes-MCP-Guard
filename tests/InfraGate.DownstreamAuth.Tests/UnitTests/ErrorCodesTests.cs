using InfraGate.DownstreamAuth;

namespace InfraGate.DownstreamAuth.Tests.UnitTests;

public sealed class ErrorCodesTests
{
    [Fact]
    public void DownstreamAuthRequired_HasCorrectValue()
    {
        Assert.Equal("downstream_auth_required", DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired);
    }
}
