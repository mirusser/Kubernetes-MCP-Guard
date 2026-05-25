namespace InfraGate.Planner.Decision;

internal static class OperationArgumentValidator
{
    public static bool TryNormalize(
        RemediationDecision decision,
        out IReadOnlyDictionary<string, object?> normalizedArguments)
    {
        normalizedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        return decision.OperationType switch
        {
            PlannerConventions.OperationTypes.RestartDeployment =>
                TryNormalizeRestartDeployment(decision.Arguments, out normalizedArguments),
            PlannerConventions.OperationTypes.ScaleDeployment =>
                TryNormalizeScaleDeployment(decision.Arguments, out normalizedArguments),
            _ => false,
        };
    }

    private static bool TryNormalizeRestartDeployment(
        IReadOnlyDictionary<string, object?> arguments,
        out IReadOnlyDictionary<string, object?> normalizedArguments)
    {
        normalizedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!TryGetRequiredString(arguments, PlannerConventions.ToolArguments.Name, out var name) ||
            !TryGetRequiredString(arguments, PlannerConventions.ToolArguments.Namespace, out var namespaceName))
        {
            return false;
        }

        normalizedArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PlannerConventions.ToolArguments.Name] = name,
            [PlannerConventions.ToolArguments.Namespace] = namespaceName,
        };
        return true;
    }

    private static bool TryNormalizeScaleDeployment(
        IReadOnlyDictionary<string, object?> arguments,
        out IReadOnlyDictionary<string, object?> normalizedArguments)
    {
        normalizedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!TryGetRequiredString(arguments, PlannerConventions.ToolArguments.Name, out var name) ||
            !TryGetRequiredString(arguments, PlannerConventions.ToolArguments.Namespace, out var namespaceName) ||
            !TryGetNonNegativeInt(arguments, PlannerConventions.ToolArguments.Replicas, out int replicas))
        {
            return false;
        }

        normalizedArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PlannerConventions.ToolArguments.Name] = name,
            [PlannerConventions.ToolArguments.Namespace] = namespaceName,
            [PlannerConventions.ToolArguments.Replicas] = replicas,
        };
        return true;
    }

    private static bool TryGetRequiredString(
        IReadOnlyDictionary<string, object?> arguments,
        string key,
        out string value)
    {
        value = string.Empty;

        if (!arguments.TryGetValue(key, out var rawValue) ||
            rawValue is not string stringValue ||
            string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        value = stringValue;
        return true;
    }

    private static bool TryGetNonNegativeInt(
        IReadOnlyDictionary<string, object?> arguments,
        string key,
        out int value)
    {
        value = 0;

        if (!arguments.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        if (rawValue is int intValue && intValue >= 0)
        {
            value = intValue;
            return true;
        }

        if (rawValue is long longValue &&
            longValue >= 0 &&
            longValue <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        return false;
    }
}
