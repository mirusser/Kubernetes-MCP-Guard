namespace InfraGate.Planner.Handoff;

internal sealed class JsonFileRemediationProposalSink : IRemediationProposalSink
{
    private readonly string rootDirectory;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public JsonFileRemediationProposalSink(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        this.rootDirectory = rootDirectory;
    }

    public async Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Proposals.Count == 0)
        {
            return;
        }

        if (!Directory.Exists(rootDirectory))
        {
            Directory.CreateDirectory(rootDirectory);
        }

        var fileName = $"{batch.CycleId}.json";
        var tmpPath = Path.Combine(rootDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(rootDirectory, fileName);

        var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        try
        {
            await JsonSerializer.SerializeAsync(stream, batch, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        File.Move(tmpPath, finalPath, overwrite: true);
    }
}
