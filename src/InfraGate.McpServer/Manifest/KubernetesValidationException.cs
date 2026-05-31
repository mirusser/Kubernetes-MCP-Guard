namespace InfraGate.McpServer;

public sealed class KubernetesValidationException : Exception
{
    public KubernetesValidationException(string message)
        : base(message)
    {
    }
}
