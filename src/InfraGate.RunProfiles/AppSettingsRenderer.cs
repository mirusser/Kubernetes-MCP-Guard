using System.Text;
using System.Text.Json;

namespace InfraGate.RunProfiles;

internal static class AppSettingsRenderer
{
    public static string Render(string configFileName, RunProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(configFileName);
        ArgumentNullException.ThrowIfNull(profile);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            WriteGeneratedMetadata(writer, configFileName, profile);
            writer.WritePropertyName(RunProfileConventions.AppSettings.Root);
            writer.WriteStartObject();
            WriteRuntime(writer, profile);
            WriteGateway(writer, profile);
            WriteAuth(writer, profile);
            WriteApproval(writer, profile);
            WriteKubernetes(writer, profile);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static void WriteGeneratedMetadata(Utf8JsonWriter writer, string configFileName, RunProfile profile)
    {
        writer.WritePropertyName(RunProfileConventions.GeneratedFile.MetadataSection);
        writer.WriteStartObject();
        writer.WriteString(RunProfileConventions.GeneratedFile.MetadataSource, configFileName);
        writer.WriteString(RunProfileConventions.GeneratedFile.MetadataProfile, profile.Name);
        writer.WriteEndObject();
    }

    private static void WriteRuntime(Utf8JsonWriter writer, RunProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RuntimeMode))
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Runtime);
        writer.WriteStartObject();
        writer.WriteString(RunProfileConventions.AppSettings.Environment, profile.RuntimeMode);
        writer.WriteEndObject();
    }

    private static void WriteGateway(Utf8JsonWriter writer, RunProfile profile)
    {
        if (profile.Gateway is null)
        {
            return;
        }

        GatewayProfile gateway = profile.Gateway;
        bool hasAnyValue =
            !string.IsNullOrEmpty(gateway.AspnetcoreUrls) ||
            !string.IsNullOrEmpty(gateway.DownstreamAssembly) ||
            !string.IsNullOrEmpty(gateway.GuardAuditRoot);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Gateway);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.AspNetCoreUrls, gateway.AspnetcoreUrls);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.DownstreamAssembly, gateway.DownstreamAssembly);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.GuardAuditRoot, gateway.GuardAuditRoot);
        writer.WriteEndObject();
    }

    private static void WriteAuth(Utf8JsonWriter writer, RunProfile profile)
    {
        IdentityProviderProfile? idp = profile.IdentityProvider;
        ApprovalAuthorityProfile? approvalAuthority = profile.ApprovalAuthority;
        if (idp is null && approvalAuthority is null)
        {
            return;
        }

        bool hasAnyValue =
            !string.IsNullOrEmpty(idp?.Authority) ||
            !string.IsNullOrEmpty(idp?.MetadataAddress) ||
            !string.IsNullOrEmpty(idp?.Resource) ||
            !string.IsNullOrEmpty(idp?.Scope) ||
            !string.IsNullOrEmpty(idp?.RequireHttpsMetadata) ||
            !string.IsNullOrEmpty(approvalAuthority?.OauthClientId) ||
            !string.IsNullOrEmpty(approvalAuthority?.OauthCallbackPath) ||
            !string.IsNullOrEmpty(approvalAuthority?.OauthAuthorizationEndpoint) ||
            !string.IsNullOrEmpty(approvalAuthority?.OauthTokenEndpoint);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Auth);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.OAuthAuthority, idp?.Authority);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.OAuthMetadataAddress, idp?.MetadataAddress);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.OAuthResource, idp?.Resource);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.OAuthScope, idp?.Scope);
        WriteBooleanIfSet(writer, RunProfileConventions.AppSettings.OAuthRequireHttpsMetadata, idp?.RequireHttpsMetadata);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ApprovalOAuthClientId, approvalAuthority?.OauthClientId);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ApprovalOAuthCallbackPath, approvalAuthority?.OauthCallbackPath);
        WriteStringIfSet(
            writer,
            RunProfileConventions.AppSettings.ApprovalOAuthAuthorizationEndpoint,
            approvalAuthority?.OauthAuthorizationEndpoint);
        WriteStringIfSet(
            writer,
            RunProfileConventions.AppSettings.ApprovalOAuthTokenEndpoint,
            approvalAuthority?.OauthTokenEndpoint);
        writer.WriteEndObject();
    }

    private static void WriteApproval(Utf8JsonWriter writer, RunProfile profile)
    {
        bool hasAnyValue =
            !string.IsNullOrEmpty(profile.GenericApprovalCore?.ApprovalRoot) ||
            !string.IsNullOrEmpty(profile.GenericApprovalCore?.PostgresConnectionString) ||
            profile.GenericApprovalCore?.RunMigrationsOnStartup is not null ||
            !string.IsNullOrEmpty(profile.ApprovalAuthority?.BaseUrl);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Approval);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.RootPath, profile.GenericApprovalCore?.ApprovalRoot);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.BaseUrl, profile.ApprovalAuthority?.BaseUrl);
        if (!string.IsNullOrEmpty(profile.GenericApprovalCore?.PostgresConnectionString) ||
            profile.GenericApprovalCore?.RunMigrationsOnStartup is not null)
        {
            writer.WritePropertyName(RunProfileConventions.AppSettings.Postgres);
            writer.WriteStartObject();
            WriteStringIfSet(writer, RunProfileConventions.AppSettings.PostgresConnectionString, profile.GenericApprovalCore.PostgresConnectionString);
            if (profile.GenericApprovalCore.RunMigrationsOnStartup is { } migrationsFlag)
            {
                writer.WriteBoolean(RunProfileConventions.AppSettings.RunMigrationsOnStartup, migrationsFlag);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteKubernetes(Utf8JsonWriter writer, RunProfile profile)
    {
        DomainAdapterProfile? adapter = profile.DomainAdapters.SingleOrDefault(adapter =>
            string.Equals(adapter.Type, RunProfileConventions.DomainAdapterTypes.Kubernetes, StringComparison.Ordinal));
        if (adapter?.Kubernetes is null)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Kubernetes);
        writer.WriteStartObject();
        writer.WriteString(RunProfileConventions.AppSettings.KubeConfig, adapter.Kubernetes.KubeConfig);
        writer.WritePropertyName(RunProfileConventions.AppSettings.AllowedNamespaces);
        writer.WriteStartArray();
        foreach (string namespaceName in adapter.Kubernetes.AllowedNamespaces)
        {
            writer.WriteStringValue(namespaceName);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStringIfSet(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteBooleanIfSet(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!bool.TryParse(value, out bool result))
        {
            throw new InvalidOperationException(
                $"{RunProfileConventions.YamlKeys.RequireHttpsMetadata} must be 'true' or 'false'; value '{value}' is not supported.");
        }

        writer.WriteBoolean(propertyName, result);
    }
}
