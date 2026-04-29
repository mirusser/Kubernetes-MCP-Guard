namespace InfraGate.McpServer;

public sealed class K8sValidationException : Exception
{
    public K8sValidationException(string message)
        : base(message)
    {
    }
}
