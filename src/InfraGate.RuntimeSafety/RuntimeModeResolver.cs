using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety;

public static class RuntimeModeResolver
{
    public static RuntimeMode FromEnvironment()
    {
        string? infraGateEnvironment = Environment.GetEnvironmentVariable(
            RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment);

        if (!string.IsNullOrWhiteSpace(infraGateEnvironment))
        {
            return ParseInfraGateEnvironment(infraGateEnvironment);
        }

        string? dotNetEnvironment = Environment.GetEnvironmentVariable(
            RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment);
        if (!string.IsNullOrWhiteSpace(dotNetEnvironment))
        {
            return ParseStandardEnvironment(dotNetEnvironment);
        }

        string? aspNetCoreEnvironment = Environment.GetEnvironmentVariable(
            RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment);
        if (!string.IsNullOrWhiteSpace(aspNetCoreEnvironment))
        {
            return ParseStandardEnvironment(aspNetCoreEnvironment);
        }

        return RuntimeMode.Development;
    }

    public static RuntimeMode FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? infraGateEnvironment = configuration[
            RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment];
        if (!string.IsNullOrWhiteSpace(infraGateEnvironment))
        {
            return ParseInfraGateEnvironment(infraGateEnvironment);
        }

        string? configuredRuntimeEnvironment = configuration[
            RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment];
        if (!string.IsNullOrWhiteSpace(configuredRuntimeEnvironment))
        {
            return ParseInfraGateEnvironment(configuredRuntimeEnvironment);
        }

        string? dotNetEnvironment = configuration[
            RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment];
        if (!string.IsNullOrWhiteSpace(dotNetEnvironment))
        {
            return ParseStandardEnvironment(dotNetEnvironment);
        }

        string? aspNetCoreEnvironment = configuration[
            RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment];
        if (!string.IsNullOrWhiteSpace(aspNetCoreEnvironment))
        {
            return ParseStandardEnvironment(aspNetCoreEnvironment);
        }

        return RuntimeMode.Development;
    }

    private static RuntimeMode ParseInfraGateEnvironment(string value)
    {
        if (string.Equals(value, RuntimeSafetyConventions.EnvironmentValues.Development, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeMode.Development;
        }

        if (string.Equals(value, RuntimeSafetyConventions.EnvironmentValues.Production, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeMode.Production;
        }

        throw new InvalidOperationException(
            $"{RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment} must be " +
            $"{RuntimeSafetyConventions.EnvironmentValues.Development} or " +
            $"{RuntimeSafetyConventions.EnvironmentValues.Production}; value '{value}' is not supported.");
    }

    private static RuntimeMode ParseStandardEnvironment(string value) =>
        string.Equals(value, RuntimeSafetyConventions.EnvironmentValues.Development, StringComparison.OrdinalIgnoreCase)
            ? RuntimeMode.Development
            : RuntimeMode.Production;
}
