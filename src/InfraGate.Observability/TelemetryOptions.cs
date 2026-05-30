namespace InfraGate.Observability;

public sealed record class TelemetryOptions
{
    public string ServiceName { get; set; } = "infragate";
    public string ServiceVersion { get; set; } = "1.0.0";
    public IReadOnlyList<string> MeterNames { get; set; } = [];
    public string? OtlpEndpoint { get; set; }
}
