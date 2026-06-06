using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Execution;

internal sealed record class OperationDispatch
{
    public OperationDispatch(
        string dryRunTool,
        string mutationTool,
        Func<KubernetesPlanPayload, Dictionary<string, object?>> argsBuilder)
    {
        DryRunTool = dryRunTool;
        MutationTool = mutationTool;
        ArgsBuilder = argsBuilder;
    }

    public string DryRunTool { get; }

    public string MutationTool { get; }

    public Func<KubernetesPlanPayload, Dictionary<string, object?>> ArgsBuilder { get; }
}
