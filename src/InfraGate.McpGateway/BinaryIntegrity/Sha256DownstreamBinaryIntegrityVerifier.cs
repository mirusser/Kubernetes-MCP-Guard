using System.Security.Cryptography;

namespace InfraGate.McpGateway.BinaryIntegrity;

internal sealed class Sha256DownstreamBinaryIntegrityVerifier : IDownstreamBinaryIntegrityVerifier
{
    public const string AlgorithmName = "SHA-256";
    private const int Sha256HexLength = 64;

    public void Verify(string assemblyPath, string expectedHashHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        if (string.IsNullOrWhiteSpace(expectedHashHex) || expectedHashHex.Length != Sha256HexLength)
        {
            throw new FormatException(
                $"Expected SHA-256 hash must be exactly {Sha256HexLength} hexadecimal characters.");
        }

        byte[] expectedHash = Convert.FromHexString(expectedHashHex);

        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            byte[] actualHash = SHA256.HashData(stream);

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new DownstreamBinaryIntegrityException(
                    assemblyPath,
                    AlgorithmName,
                    $"Downstream assembly hash mismatch: '{assemblyPath}'.");
            }
        }
        catch (IOException ex)
        {
            throw new DownstreamBinaryIntegrityException(
                assemblyPath,
                AlgorithmName,
                $"Downstream assembly could not be read: '{assemblyPath}'.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DownstreamBinaryIntegrityException(
                assemblyPath,
                AlgorithmName,
                $"Downstream assembly could not be read: '{assemblyPath}'.",
                ex);
        }
    }
}
