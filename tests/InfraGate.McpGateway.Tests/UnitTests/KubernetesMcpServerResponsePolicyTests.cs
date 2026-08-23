using System.Text;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class KubernetesMcpServerResponsePolicyTests
{
    private readonly KubernetesMcpServerResponsePolicy policy = new();

    [Theory]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool, "pod/demo Running")]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsGetTool, "pod/demo")]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsLogTool, "nginx started")]
    public void Apply_ApprovedToolResponse_PreservesUsefulText(string toolName, string responseText)
    {
        KubernetesMcpServerResponsePolicyResult result = policy.Apply(toolName, responseText);

        Assert.True(result.IsAllowed, result.Error);
        Assert.Equal(responseText, result.Text);
        Assert.Equal(Encoding.UTF8.GetByteCount(responseText), result.Utf8ByteCount);
    }

    [Fact]
    public void Apply_ResponseAtByteLimit_AllowsText()
    {
        string responseText = new('a', KubernetesMcpServerResponsePolicy.MaximumResponseBytes);

        KubernetesMcpServerResponsePolicyResult result = policy.Apply(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            responseText);

        Assert.True(result.IsAllowed, result.Error);
        Assert.Equal(KubernetesMcpServerResponsePolicy.MaximumResponseBytes, result.Utf8ByteCount);
    }

    [Fact]
    public void Apply_ResponseAboveByteLimit_RejectsText()
    {
        string responseText = new('a', KubernetesMcpServerResponsePolicy.MaximumResponseBytes + 1);

        KubernetesMcpServerResponsePolicyResult result = policy.Apply(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            responseText);

        Assert.False(result.IsAllowed);
        Assert.Empty(result.Text);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Apply_MultibyteResponseAtByteLimit_AllowsText()
    {
        string responseText = new('ą', KubernetesMcpServerResponsePolicy.MaximumResponseBytes / 2);

        KubernetesMcpServerResponsePolicyResult result = policy.Apply(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            responseText);

        Assert.True(result.IsAllowed, result.Error);
        Assert.Equal(KubernetesMcpServerResponsePolicy.MaximumResponseBytes, result.Utf8ByteCount);
    }

    [Fact]
    public void Apply_MultibyteResponseAboveByteLimit_RejectsText()
    {
        string responseText = new('ą', (KubernetesMcpServerResponsePolicy.MaximumResponseBytes / 2) + 1);

        KubernetesMcpServerResponsePolicyResult result = policy.Apply(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            responseText);

        Assert.False(result.IsAllowed);
        Assert.True(result.Utf8ByteCount > KubernetesMcpServerResponsePolicy.MaximumResponseBytes);
    }

    [Theory]
    [InlineData("events_list")]
    [InlineData("resources_get")]
    public void Apply_UnapprovedTool_RejectsText(string toolName)
    {
        KubernetesMcpServerResponsePolicyResult result = policy.Apply(toolName, "raw object");

        Assert.False(result.IsAllowed);
        Assert.Empty(result.Text);
    }
}
