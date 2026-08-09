namespace InfraGate.McpGateway;

internal sealed record class KubernetesMcpServerResponsePolicyResult(
    bool IsAllowed,
    string Text,
    string Error,
    int Utf8ByteCount);
