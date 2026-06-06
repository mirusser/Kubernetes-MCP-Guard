using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;
using InfraGate.KubernetesAdapter.Execution;
using InfraGate.KubernetesAdapter.Approval;
using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.KubernetesAdapter;

public static class KubernetesAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddKubernetesAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IKubernetesEvidenceService, KubernetesEvidenceService>();
        services.AddSingleton(sp => new KubernetesPlanBuilder(sp.GetRequiredService<IKubernetesEvidenceService>()));
        services.AddSingleton(sp => new KubernetesPlanExecutor(
            sp.GetRequiredService<IToolCaller>(),
            sp.GetRequiredService<IKubernetesEvidenceService>(),
            sp.GetService<IApprovalAuditOutbox>()));
        services.AddSingleton<KubernetesPlanReviewAdapter>();
        services.AddSingleton<IDomainPlanBuilder>(sp => sp.GetRequiredService<KubernetesPlanBuilder>());
        services.AddSingleton<IDomainPlanExecutor>(sp => sp.GetRequiredService<KubernetesPlanExecutor>());
        services.AddSingleton<IPlanReviewAdapter>(sp => sp.GetRequiredService<KubernetesPlanReviewAdapter>());
        services.AddSingleton<IDomainAdapter, KubernetesDomainAdapter>();
        return services;
    }
}
