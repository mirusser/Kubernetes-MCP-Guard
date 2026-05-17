namespace InfraGate.KubernetesAdapter;

public sealed record KubernetesPlanDecodeResult(bool Succeeded, KubernetesPlan? Plan, string Message)
{
    public static KubernetesPlanDecodeResult Success(KubernetesPlan plan) =>
        new(true, plan, "Decoded.");

    public static KubernetesPlanDecodeResult Failed(string message) =>
        new(false, null, message);
}
