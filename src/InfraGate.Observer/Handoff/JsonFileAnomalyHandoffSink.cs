namespace InfraGate.Observer.Handoff;

internal sealed class JsonFileAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly string rootDirectory;
    private readonly ILogger<JsonFileAnomalyHandoffSink> logger;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public JsonFileAnomalyHandoffSink(string rootDirectory, ILogger<JsonFileAnomalyHandoffSink> logger)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        this.rootDirectory = rootDirectory;
        this.logger = logger;
    }

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Reports.Count == 0)
        {
            return;
        }

        if (!Directory.Exists(rootDirectory))
        {
            logger.LogInformation("File sink directory does not exist, creating: {Directory}", rootDirectory);
            Directory.CreateDirectory(rootDirectory);
        }

        var fileName = $"{batch.CycleId}.json";
        var tmpPath = Path.Combine(rootDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(rootDirectory, fileName);

        var json = JsonSerializer.Serialize(batch, SerializerOptions);
        logger.LogInformation(
            "Writing anomaly findings to file sink: path={FinalPath} cycleId={CycleId} reportCount={ReportCount}\n{Json}",
            finalPath, batch.CycleId, batch.Reports.Count, json);

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
        logger.LogInformation("Anomaly findings written to {FinalPath}", finalPath);
    }
}
