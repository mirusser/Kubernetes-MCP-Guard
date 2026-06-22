namespace InfraGate.McpGateway.BinaryIntegrity;

internal sealed class DownstreamBinaryIntegrityException : Exception
{
    public DownstreamBinaryIntegrityException(string filePath, string algorithm, string message)
        : base(message)
    {
        FilePath = filePath;
        Algorithm = algorithm;
    }

    public DownstreamBinaryIntegrityException(string filePath, string algorithm, string message, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
        Algorithm = algorithm;
    }

    public string FilePath { get; }
    public string Algorithm { get; }
}
