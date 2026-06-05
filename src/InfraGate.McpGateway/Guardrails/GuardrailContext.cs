namespace InfraGate.McpGateway;

/// <summary>
/// AsyncLocal-based guardrail context for tracking response sanitization
/// findings across the async call chain during a single request.
/// </summary>
internal static class GuardrailContext
{
    private static readonly AsyncLocal<bool> hasResponseFindings = new();

    /// <summary>
    /// True when response sanitization detected findings during the current request scope.
    /// </summary>
    public static bool HasResponseFindings => hasResponseFindings.Value;

    /// <summary>
    /// Mark that response guardrail findings were detected.
    /// </summary>
    public static void MarkResponseFindings()
    {
        hasResponseFindings.Value = true;
    }

    /// <summary>
    /// Reset the context for a new request scope.
    /// Called at the start of each guarded tool call to prevent cross-request contamination.
    /// </summary>
    public static void Reset()
    {
        hasResponseFindings.Value = false;
    }
}
