using System.Text;

namespace InfraGate.RunProfiles;

internal static class EnvFileRenderer
{
    public static string Render(string configFileName, RunProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(configFileName);
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new StringBuilder();
        builder.AppendLine($"# Generated from {configFileName} profile: {profile.Name}");
        builder.AppendLine($"# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate {profile.Name}");

        AppendRuntime(builder, profile);
        AppendGenericApprovalCore(builder, profile);
        AppendKubernetesAdapter(builder, profile);

        return builder.ToString();
    }

    private static void AppendRuntime(StringBuilder builder, RunProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RuntimeMode))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Runtime");
        builder.AppendLine($"{RunProfileConventions.Env.InfraGateEnvironment}={profile.RuntimeMode}");
    }

    private static void AppendGenericApprovalCore(StringBuilder builder, RunProfile profile)
    {
        if (profile.GenericApprovalCore is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Generic Approval Core");
        builder.AppendLine($"{RunProfileConventions.Env.ApprovalRoot}={profile.GenericApprovalCore.ApprovalRoot}");
    }

    private static void AppendKubernetesAdapter(StringBuilder builder, RunProfile profile)
    {
        DomainAdapterProfile? adapter = profile.DomainAdapters.SingleOrDefault(adapter =>
            string.Equals(adapter.Type, RunProfileConventions.DomainAdapterTypes.Kubernetes, StringComparison.Ordinal));
        if (adapter?.Kubernetes is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Kubernetes Adapter");
        builder.AppendLine($"{RunProfileConventions.Env.KubeConfig}={adapter.Kubernetes.KubeConfig}");
        builder.AppendLine(
            $"{RunProfileConventions.Env.AllowedNamespaces}={string.Join(',', adapter.Kubernetes.AllowedNamespaces)}");
    }
}
