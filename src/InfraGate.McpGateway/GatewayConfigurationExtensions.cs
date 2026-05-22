using InfraGate.Approvals;
using InfraGate.McpGateway.Auth;
using InfraGate.RuntimeSafety;

namespace InfraGate.McpGateway;

internal static class GatewayConfigurationExtensions
{
    internal static void AddInfraGateConfiguration(IConfigurationBuilder configuration, string[] args)
    {
        string? configPath = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
            configuration.AddInfraGateEnvironmentVariables(mappings =>
            {
                RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);
                McpGatewayConventions.RegisterInfraGateEnvVarMappings(mappings);
                GatewayAuthConventions.RegisterInfraGateEnvVarMappings(mappings);
                mappings.Map(ApprovalConventions.EnvironmentVariables.ApprovalRoot, McpGatewayConventions.ConfigurationKeys.ApprovalRoot);
            });
            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(args);
        }
    }
}
