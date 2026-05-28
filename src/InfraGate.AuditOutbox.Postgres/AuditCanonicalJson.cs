using InfraGate.Approvals;

namespace InfraGate.AuditOutbox.Postgres;

public static class AuditCanonicalJson
{
    public static string Serialize(object? value) =>
        ApprovalCanonicalJson.Serialize(value);

    public static string ComputeSha256Hex(string canonicalText) =>
        ApprovalCanonicalJson.ComputeSha256Hex(canonicalText);
}
