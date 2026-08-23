namespace InfraGate.McpGateway.Tests.Fakes;

/// <summary>
/// Fake secondary downstream client implementing <see cref="ISupervisedDownstreamStatus"/> so
/// <c>GatewayReadinessChecker</c> tests can drive every reported state without a real supervised
/// subprocess (already covered by <c>DownstreamProcessSupervisorTests</c>).
/// </summary>
internal sealed class FakeSupervisedDownstreamMcpClient : IDownstreamMcpClient, ISupervisedDownstreamStatus
{
    public long ProcessGeneration { get; set; } = 1;

    public bool IsRestarting { get; set; }

    public Exception? ListToolsException { get; set; }

    public Task<DownstreamCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Readiness checks do not call tools.");
    }

    public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        return ListToolsException is not null
            ? Task.FromException<IReadOnlyList<DownstreamTool>>(ListToolsException)
            : Task.FromResult<IReadOnlyList<DownstreamTool>>([]);
    }
}
