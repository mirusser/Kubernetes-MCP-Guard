using InfraGate.RfcRag.Settings;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class RfcRagOptionsTests
{
    [Fact]
    public void EmbeddingDimensions_DefaultValue_Is1536()
    {
        var options = new RfcRagOptions();

        Assert.Equal(1536, options.EmbeddingDimensions);
    }
}
