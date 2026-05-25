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
            WriteObserver(writer, profile);
            WritePlanner(writer, profile);
            WriteExecutor(writer, profile);
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

    private static void WritePlanner(Utf8JsonWriter writer, RunProfile profile)
    {
        if (profile.Planner is null)
        {
            return;
        }

        PlannerProfile planner = profile.Planner;
        bool hasAnyValue =
            !string.IsNullOrEmpty(planner.GatewayBaseUrl) ||
            !string.IsNullOrEmpty(planner.ExecutorHandoffUrl) ||
            !string.IsNullOrEmpty(planner.TokenEndpoint) ||
            !string.IsNullOrEmpty(planner.ClientId) ||
            !string.IsNullOrEmpty(planner.ClientSecret) ||
            !string.IsNullOrEmpty(planner.LlmProvider) ||
            !string.IsNullOrEmpty(planner.LlmModel) ||
            !string.IsNullOrEmpty(planner.LlmApiKey) ||
            !string.IsNullOrEmpty(planner.AnomalyWallClockCapSeconds) ||
            !string.IsNullOrEmpty(planner.BatchWallClockCapSeconds) ||
            !string.IsNullOrEmpty(planner.MaxToolIterations) ||
            !string.IsNullOrEmpty(planner.FileSinkRoot);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Planner);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerGatewayBaseUrl, planner.GatewayBaseUrl);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerExecutorHandoffUrl, planner.ExecutorHandoffUrl);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerTokenEndpoint, planner.TokenEndpoint);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerClientId, planner.ClientId);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerClientSecret, planner.ClientSecret);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerLlmProvider, planner.LlmProvider);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerLlmModel, planner.LlmModel);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerLlmApiKey, planner.LlmApiKey);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerAnomalyWallClockCapSeconds, planner.AnomalyWallClockCapSeconds);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerBatchWallClockCapSeconds, planner.BatchWallClockCapSeconds);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerMaxToolIterations, planner.MaxToolIterations);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.PlannerFileSinkRoot, planner.FileSinkRoot);
        writer.WriteEndObject();
    }

    private static void WriteExecutor(Utf8JsonWriter writer, RunProfile profile)
    {
        if (profile.Executor is null)
        {
            return;
        }

        ExecutorProfile executor = profile.Executor;
        bool hasAnyValue =
            !string.IsNullOrEmpty(executor.GatewayBaseUrl) ||
            !string.IsNullOrEmpty(executor.TokenEndpoint) ||
            !string.IsNullOrEmpty(executor.ClientId) ||
            !string.IsNullOrEmpty(executor.ClientSecret) ||
            !string.IsNullOrEmpty(executor.ConcurrencyCap) ||
            !string.IsNullOrEmpty(executor.WatchTimeoutSeconds);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Executor);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorGatewayBaseUrl, executor.GatewayBaseUrl);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorTokenEndpoint, executor.TokenEndpoint);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorClientId, executor.ClientId);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorClientSecret, executor.ClientSecret);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorConcurrencyCap, executor.ConcurrencyCap);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ExecutorWatchTimeoutSeconds, executor.WatchTimeoutSeconds);
        writer.WriteEndObject();
    }

    private static void WriteObserver(Utf8JsonWriter writer, RunProfile profile)
    {
        if (profile.Observer is null)
        {
            return;
        }

        ObserverProfile observer = profile.Observer;
        bool hasAnyValue =
            !string.IsNullOrEmpty(observer.GatewayBaseUrl) ||
            !string.IsNullOrEmpty(observer.TokenEndpoint) ||
            !string.IsNullOrEmpty(observer.ClientId) ||
            !string.IsNullOrEmpty(observer.ClientSecret) ||
            !string.IsNullOrEmpty(observer.Scope) ||
            !string.IsNullOrEmpty(observer.LlmProvider) ||
            !string.IsNullOrEmpty(observer.LlmModel) ||
            !string.IsNullOrEmpty(observer.LlmApiKey) ||
            !string.IsNullOrEmpty(observer.CycleCadenceSeconds) ||
            !string.IsNullOrEmpty(observer.CycleWallClockCapSeconds) ||
            !string.IsNullOrEmpty(observer.MaxToolIterations) ||
            !string.IsNullOrEmpty(observer.FileSinkRoot);

        if (!hasAnyValue)
        {
            return;
        }

        writer.WritePropertyName(RunProfileConventions.AppSettings.Observer);
        writer.WriteStartObject();
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverGatewayBaseUrl, observer.GatewayBaseUrl);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverTokenEndpoint, observer.TokenEndpoint);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverClientId, observer.ClientId);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverClientSecret, observer.ClientSecret);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverScope, observer.Scope);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverLlmProvider, observer.LlmProvider);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverLlmModel, observer.LlmModel);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverLlmApiKey, observer.LlmApiKey);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverCycleIntervalSeconds, observer.CycleCadenceSeconds);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverCycleWallClockCapSeconds, observer.CycleWallClockCapSeconds);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverMaxToolIterations, observer.MaxToolIterations);
        WriteStringIfSet(writer, RunProfileConventions.AppSettings.ObserverFileSinkRoot, observer.FileSinkRoot);
        writer.WriteEndObject();
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
