using InfraGate.McpGateway.BinaryIntegrity;

namespace InfraGate.McpGateway.Tests.Fakes;

internal sealed class FakeDownstreamBinaryIntegrityVerifier : IDownstreamBinaryIntegrityVerifier
{
    private readonly IReadOnlyDictionary<(string Path, string ExpectedHash), bool> outcomes;

    public FakeDownstreamBinaryIntegrityVerifier(IReadOnlyDictionary<(string Path, string ExpectedHash), bool> outcomes)
    {
        this.outcomes = outcomes;
    }

    public IReadOnlyList<(string Path, string ExpectedHash)> Calls { get; private set; } = [];

    public void Verify(string assemblyPath, string expectedHashHex)
    {
        Calls = Calls.Append((assemblyPath, expectedHashHex)).ToList();

        if (!outcomes.TryGetValue((assemblyPath, expectedHashHex), out bool matches) || !matches)
        {
            throw new DownstreamBinaryIntegrityException(
                assemblyPath,
                Sha256DownstreamBinaryIntegrityVerifier.AlgorithmName,
                $"Hash mismatch for '{assemblyPath}'.");
        }
    }
}
