using InfraGate.ClientCredentials;

namespace InfraGate.ClientCredentials.Tests.UnitTests;

public sealed class ClientCredentialsConventionsTests
{
    [Fact]
    public void BearerPrefix_IsBearerWithSpace()
    {
        Assert.Equal("Bearer ", ClientCredentialsConventions.BearerPrefix);
    }

    [Fact]
    public void DefaultRefreshSkewSeconds_Is30()
    {
        Assert.Equal(30, ClientCredentialsConventions.DefaultRefreshSkewSeconds);
    }
}
