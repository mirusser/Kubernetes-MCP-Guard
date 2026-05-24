namespace InfraGate.Observer.Handoff;

internal sealed class JsonFileAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly string rootDirectory;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public JsonFileAnomalyHandoffSink(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        this.rootDirectory = rootDirectory;
    }

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Reports.Count == 0)
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
