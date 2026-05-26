using InfraGate.McpGateway.Email;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalEmailRendererTests
{
    [Fact]
    public void RenderPlaintext_IncludesCodeUrlSummaryAndExpiryWithinTenLines()
    {
        var body = ApprovalEmailRenderer.RenderPlaintext(new ApprovalEmailTemplateData(
            "plan-1",
            "Restart Deployment 'demo' in namespace 'mcp-nginx-demo'.",
            "ABCDEFGH",
            "https://gateway.example.com/approvals/code",
            new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero)));

        Assert.Contains("ABCDEFGH", body);
        Assert.Contains("plan-1", body);
        Assert.Contains("Restart Deployment", body);
        Assert.Contains("https://gateway.example.com/approvals/code", body);
        Assert.Contains("2026-05-24T12:30:00.0000000+00:00", body);
        Assert.True(body.Split('\n').Length <= 10);
    }
}
