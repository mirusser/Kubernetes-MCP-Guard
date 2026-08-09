using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpGateway.Notifications;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.UnitTests.Notifications;

public sealed class PlanStatusResourceHandlerTests
{
    [Fact]
    public void ListTemplates_Always_ReturnsPlanStatusTemplate()
    {
        var handler = CreateHandler();

        var result = handler.ListTemplates();

        var template = Assert.Single(result.ResourceTemplates);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusTemplateName, template.Name);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusUriTemplate, template.UriTemplate);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusMimeType, template.MimeType);
    }

    [Fact]
    public async Task ReadAsync_UnknownSafePlan_ReturnsNotFoundJson()
    {
        var handler = CreateHandler();
        string planId = ApprovalIds.NewPlanId();

        var result = await handler.ReadAsync(
            new ReadResourceRequestParams { Uri = NotificationsConventions.Resources.PlanStatusUri(planId) },
            CancellationToken.None);

        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal(NotificationsConventions.Resources.PlanStatusUri(planId), content.Uri);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusMimeType, content.MimeType);
        using var document = JsonDocument.Parse(content.Text);
        Assert.Equal(planId, document.RootElement.GetProperty(McpGatewayConventions.ToolArguments.PlanId).GetString());
        Assert.Equal(
            ApprovalConventions.PlanStatusValues.NotFound,
            document.RootElement.GetProperty(McpGatewayConventions.ToolResponseFields.Status).GetString());
    }

    [Theory]
    [InlineData("file:///tmp/plan/status")]
    [InlineData("plan://")]
    [InlineData("plan://abc")]
    [InlineData("plan://abc/other")]
    [InlineData("plan://../status")]
    public async Task ReadAsync_MalformedOrUnsupportedUri_ThrowsMcpException(string uri)
    {
        var handler = CreateHandler();

        await Assert.ThrowsAsync<McpException>(() =>
            handler.ReadAsync(new ReadResourceRequestParams { Uri = uri }, CancellationToken.None));
    }

    private static PlanStatusResourceHandler CreateHandler()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-plan-resource-tests", Guid.NewGuid().ToString("N"));
        var store = new ApprovalStore(new ApprovalStoreOptions(root));

        return new PlanStatusResourceHandler(store);
    }
}
