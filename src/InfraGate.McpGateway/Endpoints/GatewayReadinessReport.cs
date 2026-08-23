namespace InfraGate.McpGateway.Endpoints;

/// <summary>
/// Lifecycle state of the optional secondary (Kubernetes MCP server) downstream, as surfaced by
/// <c>/readyz</c>. Never affects the endpoint's overall status code — see
/// <see cref="GatewayReadinessReport.IsReady"/>.
/// </summary>
internal enum SecondaryDownstreamStatus
{
    /// <summary>The Kubernetes MCP server downstream was never configured for this deployment.</summary>
    NotConfigured,

    /// <summary>Configured, transport live, but the catalog has not yet published a validated snapshot for the current process generation.</summary>
    Starting,

    /// <summary>A restart is in progress (backoff wait or active respawn) following a detected fault.</summary>
    BackingOff,

    /// <summary>Live transport plus a catalog snapshot validated for the current process generation.</summary>
    Ready,

    /// <summary>Restart attempts were exhausted; the source is omitted from the catalog until a new fault retriggers recovery.</summary>
    Degraded
}

internal sealed record class SecondaryDownstreamHealth(
    SecondaryDownstreamStatus Status,
    long ProcessGeneration,
    long CatalogGeneration,
    string? DegradedReason);

/// <summary>
/// Result of a single <c>/readyz</c> evaluation. <see cref="IsReady"/> reflects only the
/// mandatory dependencies (Postgres, primary downstream) — the optional secondary's state is
/// informational and never fails readiness, matching Task 10/12's isolation guarantee.
/// </summary>
internal sealed record class GatewayReadinessReport(
    bool IsReady,
    bool PostgresHealthy,
    bool PrimaryHealthy,
    SecondaryDownstreamHealth Secondary);
