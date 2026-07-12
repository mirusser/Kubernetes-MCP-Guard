namespace InfraGate.McpGateway;

public sealed class DownstreamToolRegistry(IDownstreamMcpClient downstream)
{
    private IReadOnlyList<DownstreamTool>? tools;
    private readonly SemaphoreSlim initLock = new(1, 1);

    public async Task<IReadOnlyList<DownstreamTool>> GetReadOnlyAsync(CancellationToken ct)
    {
        IReadOnlyList<DownstreamTool> all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(t => t.IsReadOnly).ToList();
    }

    public async Task<IReadOnlyList<DownstreamTool>> GetDestructiveAsync(CancellationToken ct)
    {
        IReadOnlyList<DownstreamTool> all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(t => t.IsDestructive).ToList();
    }

    private async Task<IReadOnlyList<DownstreamTool>> GetAllAsync(CancellationToken ct)
    {
        if (tools is not null)
        {
            return tools;
        }

        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (tools is not null)
            {
                return tools;
            }

            tools = await downstream.ListToolsAsync(ct).ConfigureAwait(false);
            return tools;
        }
        finally
        {
            initLock.Release();
        }
    }
}
