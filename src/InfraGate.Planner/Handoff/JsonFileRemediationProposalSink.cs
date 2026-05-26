namespace InfraGate.Planner.Handoff;

internal sealed class JsonFileRemediationProposalSink : IRemediationProposalSink
{
    private readonly string rootDirectory;
    private readonly ILogger<JsonFileRemediationProposalSink> logger;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public JsonFileRemediationProposalSink(string rootDirectory, ILogger<JsonFileRemediationProposalSink> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        this.rootDirectory = rootDirectory;
        this.logger = logger;
    }

    public async Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Proposals.Count == 0)
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
            "Writing remediation proposals to file sink: path={FinalPath} cycleId={CycleId} proposalCount={ProposalCount}\n{Json}",
            finalPath, batch.CycleId, batch.Proposals.Count, json);

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
        logger.LogInformation("Remediation proposals written to {FinalPath}", finalPath);
    }
}
