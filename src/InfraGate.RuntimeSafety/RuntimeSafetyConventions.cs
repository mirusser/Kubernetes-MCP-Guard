namespace InfraGate.RuntimeSafety;

public static class RuntimeSafetyConventions
{
    public static class EnvironmentVariables
    {
        public const string InfraGateEnvironment = "INFRA_GATE_ENVIRONMENT";
        public const string DotNetEnvironment = "DOTNET_ENVIRONMENT";
        public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";
    }

    public static class EnvironmentValues
    {
        public const string Development = "Development";
        public const string Production = "Production";
    }
}
