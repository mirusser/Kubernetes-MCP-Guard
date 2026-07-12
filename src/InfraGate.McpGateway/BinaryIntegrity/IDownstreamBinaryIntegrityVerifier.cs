namespace InfraGate.McpGateway.BinaryIntegrity;

internal interface IDownstreamBinaryIntegrityVerifier
{
    void Verify(string assemblyPath, string expectedHashHex);
}
