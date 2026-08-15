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

    /// <summary>
    /// Clears the cached tool list so the next <see cref="GetReadOnlyAsync"/>/<see cref="GetDestructiveAsync"/>
    /// call re-fetches from the downstream client, e.g. after a supervised process restart
    /// replaces the underlying session. Takes the same lock as the fetch path, so it cannot race
    /// with an in-flight initial population.
    /// </summary>
    public async Task InvalidateAsync(CancellationToken ct)
    {
        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            tools = null;
        }
        finally
        {
            initLock.Release();
        }
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
