namespace InfraGate.McpGateway;

/// <summary>
/// Lifecycle state a <see cref="DownstreamProcessSupervisor"/> exposes for health/readiness
/// reporting (see <c>GatewayReadinessChecker</c>), kept separate from <see cref="IDownstreamMcpClient"/>
/// so readiness tests can fake this state without a real controllable subprocess — Task 12's
/// tests already cover whether the supervisor tracks this state correctly against a real process.
/// </summary>
internal interface ISupervisedDownstreamStatus
{
    /// <summary>
    /// Count of successful process (re)creations, starting at 1 for the original process.
    /// </summary>
    long ProcessGeneration { get; }

    /// <summary>
    /// True from the moment a fault triggers a restart loop until it either succeeds or exhausts
    /// its attempts (covers both the backoff waits between attempts and the respawn itself).
    /// </summary>
    bool IsRestarting { get; }
}
