using InfraGate.McpGateway.Email;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class SmtpClientFactoryTests
{
    [Fact]
    public void Create_WithEnableSslTrue_ConfiguresClientForTls()
    {
        var factory = new SmtpClientFactory();
        using var client = factory.Create(new SmtpApprovalEmailOptions(
            "smtp.example.com",
            587,
            "infragate@example.com",
            EnableSsl: true));

        Assert.True(client.EnableSsl);
    }
}
