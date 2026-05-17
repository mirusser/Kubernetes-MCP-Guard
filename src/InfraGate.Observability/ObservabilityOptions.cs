namespace InfraGate.Observability;

public sealed record ObservabilityOptions
{
    public bool WriteToConsole { get; set; } = true;
    public bool ConsoleToStandardError { get; set; }
    public string? FilePath { get; set; }
}
