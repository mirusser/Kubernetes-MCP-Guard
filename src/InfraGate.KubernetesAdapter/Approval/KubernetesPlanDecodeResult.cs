namespace InfraGate.KubernetesAdapter.Approval;

public sealed record class KubernetesPlanDecodeResult(
    bool Succeeded,
    KubernetesPlan? Plan,
    string Message,
    string? ReasonCode = null)
{
    public static KubernetesPlanDecodeResult Success(KubernetesPlan plan) =>
        new(true, plan, "Decoded.");

    public static KubernetesPlanDecodeResult Failed(string message, string? reasonCode = null) =>
        new(false, null, message, reasonCode);
}
