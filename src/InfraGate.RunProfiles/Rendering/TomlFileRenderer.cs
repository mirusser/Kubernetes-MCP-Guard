using System.Text;

namespace InfraGate.RunProfiles;

// Hand-rolled writer: the generated config only needs flat scalar/array-of-string
// keys, so a TOML library is not warranted.
internal static class TomlFileRenderer
{
    public static string Render(string configFileName, RunProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(configFileName);
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.KubernetesMcpServer is not { } kubernetesMcpServer)
        {
            throw new InvalidOperationException(
                $"Run Profile '{profile.Name}' has no Kubernetes MCP Server profile.");
        }

        var builder = new StringBuilder();
        builder.AppendLine(
            $"{RunProfileConventions.GeneratedFile.HeaderLinePrefix}{configFileName}{RunProfileConventions.GeneratedFile.ProfileMarker}{profile.Name}");
        builder.AppendLine(
            $"{RunProfileConventions.GeneratedFile.TomlDoNotEditLinePrefix}{profile.Name}");
        builder.AppendLine();
        builder.AppendLine($"{RunProfileConventions.Toml.KubeConfig} = {ToTomlString(kubernetesMcpServer.Kubeconfig)}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.ClusterProviderStrategy} = {ToTomlString(KubernetesMcpServerProfile.ClusterProviderStrategy)}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.ClusterAuthMode} = {ToTomlString(KubernetesMcpServerProfile.ClusterAuthMode)}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.Toolsets} = {ToTomlStringArray(KubernetesMcpServerProfile.Toolsets)}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.Stateless} = {(KubernetesMcpServerProfile.IsStateless ? "true" : "false")}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.ReadOnly} = {(KubernetesMcpServerProfile.IsReadOnly ? "true" : "false")}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.DisableDestructive} = {(KubernetesMcpServerProfile.AreDestructiveToolsDisabled ? "true" : "false")}");
        builder.AppendLine(
            $"{RunProfileConventions.Toml.EnabledTools} = {ToTomlStringArray(KubernetesMcpServerProfile.EnabledTools)}");

        return builder.ToString();
    }

    private static string ToTomlString(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return "\"" + escaped + "\"";
    }

    private static string ToTomlStringArray(IReadOnlyList<string> values) =>
        "[" + string.Join(", ", values.Select(ToTomlString)) + "]";
}
