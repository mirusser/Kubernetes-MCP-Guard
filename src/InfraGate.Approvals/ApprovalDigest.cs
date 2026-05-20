using System.Security.Cryptography;

namespace InfraGate.Approvals;

public sealed record class ApprovalDigest(string Algorithm, string Canonicalization, string Value)
{
    public static ApprovalDigest ComputeSha256(string canonicalization, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalization);

        var canonicalBytes = CanonicalJson.SerializeToUtf8Bytes(value);
        var hash = SHA256.HashData(canonicalBytes);

        return new ApprovalDigest(
            ApprovalConventions.Digests.Sha256,
            canonicalization,
            Convert.ToHexString(hash).ToUpperInvariant());
    }
}
