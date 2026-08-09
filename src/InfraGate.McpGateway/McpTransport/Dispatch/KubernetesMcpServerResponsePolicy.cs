using System.Text;

namespace InfraGate.McpGateway;

internal sealed class KubernetesMcpServerResponsePolicy
{
    internal const int MaximumResponseBytes = 256 * 1024;

    internal KubernetesMcpServerResponsePolicyResult Apply(string toolName, string sanitizedText)
    {
        ArgumentNullException.ThrowIfNull(sanitizedText);

        int utf8ByteCount = Encoding.UTF8.GetByteCount(sanitizedText);
        if (!McpGatewayConventions.SecondaryDownstream.ApprovedTools.Contains(toolName))
        {
            return new KubernetesMcpServerResponsePolicyResult(
                false,
                string.Empty,
                McpGatewayMessages.KubernetesMcpServerPolicy.ToolNotAllowed(toolName),
                utf8ByteCount);
        }

        if (utf8ByteCount > MaximumResponseBytes)
        {
            return new KubernetesMcpServerResponsePolicyResult(
                false,
                string.Empty,
                McpGatewayMessages.KubernetesMcpServerPolicy.ResponseTooLarge(
                    utf8ByteCount,
                    MaximumResponseBytes),
                utf8ByteCount);
        }

        return new KubernetesMcpServerResponsePolicyResult(true, sanitizedText, string.Empty, utf8ByteCount);
    }
}
