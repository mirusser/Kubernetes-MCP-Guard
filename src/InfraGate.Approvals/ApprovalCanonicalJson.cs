using System.Security.Cryptography;
using System.Text;

namespace InfraGate.Approvals;

public static class ApprovalCanonicalJson
{
    public static string Serialize(object? value) =>
        Encoding.UTF8.GetString(CanonicalJson.SerializeToUtf8Bytes(value));

    public static string ComputeSha256Hex(string canonicalText)
    {
        ArgumentNullException.ThrowIfNull(canonicalText);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText))).ToUpperInvariant();
    }
}
