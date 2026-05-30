namespace InfraGate.Observability;

internal static class TelemetryConventions
{
    // ActivitySource / meter names to register in the provider
    internal const string AgentsAiSourceName = "Experimental.Microsoft.Agents.AI";
    internal const string ExtensionsAiSourceName = "Experimental.Microsoft.Extensions.AI";
    internal const string WorkflowsSourceName = "Microsoft.Agents.AI.Workflows";

    // Environment variables read by the framework and by AddInfraGateTelemetry
    internal const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
    internal const string GenAiCaptureContentEnvVar = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
}
