namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed class K8sValidationException : Exception
{
    public K8sValidationException(string message)
        : base(message)
    {
    }
}
