namespace InfraGate.RuntimeSafety;

public static class RuntimeSafetyConventions
{
    extension(InfraGateEnvVarMappings mappings)
    {
        public void RegisterInfraGateEnvVarMappings()
        {
            ArgumentNullException.ThrowIfNull(mappings);
            mappings.Map(EnvironmentVariables.InfraGateEnvironment, ConfigurationKeys.InfraGateRuntimeEnvironment);
        }
    }

    public static class EnvironmentVariables
    {
        public const string ConfigPath = "INFRA_GATE_CONFIG_PATH";
        public const string InfraGateEnvironment = "INFRA_GATE_ENVIRONMENT";
        public const string DotNetEnvironment = "DOTNET_ENVIRONMENT";
        public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";
    }

    public static class ConfigurationKeys
    {
        public const string InfraGateRuntimeEnvironment = "InfraGate:Runtime:Environment";
    }

    public static class EnvironmentValues
    {
        public const string Development = "Development";
        public const string Production = "Production";
    }
}
