namespace InfraGate;

internal static class ModelVisibleToolResultConventions
{
    public const string SchemaVersion = "schemaVersion";
    public const string Kind = "kind";
    public const string ToolName = "toolName";
    public const string Source = "source";
    public const string GeneratedAtUtc = "generatedAtUtc";
    public const string Status = "status";
    public const string Guardrail = "guardrail";
    public const string GuardrailAction = "action";
    public const string GuardrailCategories = "categories";
    public const string Untrusted = "untrusted";
    public const string UntrustedPayload = "payload";

    public const string KindValue = "model_visible_tool_result";
    public const string SourceReadOnlyToolValue = "kubernetes.downstream_read_only_tool";
    public const string StatusSuccess = "success";
    public const string StatusError = "error";
    public const string GuardrailActionAllow = "allow";
}
