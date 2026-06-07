using System.Security.Cryptography;
using System.Text;

namespace InfraGate.RfcRag.Indexing;

public sealed class RfcIndexer : IIndexerService
{
    private readonly NpgsqlDataSource dataSource;
    private readonly IndexingRepository repository;
    private readonly RfcParser parser;
    private readonly EmbeddingService embeddingService;
    private readonly RfcRagOptions options;
    private readonly ILogger<RfcIndexer> logger;

    public RfcIndexer(
        NpgsqlDataSource dataSource,
        IndexingRepository repository,
        RfcParser parser,
        EmbeddingService embeddingService,
        IOptions<RfcRagOptions> options,
        ILogger<RfcIndexer> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.repository = repository;
        this.parser = parser;
        this.embeddingService = embeddingService;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task IndexAllAsync(CancellationToken cancellationToken)
    {
        string mirrorPath = ResolveMirrorPath(options.RfcMirrorPath);
        if (!Directory.Exists(mirrorPath))
        {
            throw new DirectoryNotFoundException($"RFC mirror path '{mirrorPath}' does not exist.");
        }

        IEnumerable<RfcSourceFile> sourceFiles = Directory
            .EnumerateFiles(mirrorPath, "rfc*.txt", SearchOption.AllDirectories)
            .Select(TryCreateSourceFile)
            .OfType<RfcSourceFile>();

        // Single query to load all existing hashes, avoiding N individual SELECTs during the parallel loop
        Dictionary<int, string> indexedHashes = await repository
            .GetAllIndexedHashesAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Indexing RFC source files from {MirrorPath}", mirrorPath);

        var parallelOptions = new ParallelOptions
        {
            // I/O-bound (embedding HTTP + DB writes): higher concurrency than CPU count is correct
            MaxDegreeOfParallelism = options.MaxIndexingParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            sourceFiles,
            parallelOptions,
            async (sourceFile, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                logger.LogDebug("Indexing RFC {RfcNumber}...", sourceFile.RfcNumber);
                await IndexFileAsync(sourceFile, mirrorPath, force: false, indexedHashes, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    public async Task IndexSingleAsync(int rfcNumber, bool force, CancellationToken cancellationToken)
    {
        string mirrorPath = ResolveMirrorPath(options.RfcMirrorPath);
        if (!Directory.Exists(mirrorPath))
        {
            throw new DirectoryNotFoundException($"RFC mirror path '{mirrorPath}' does not exist.");
        }

        string filePath = Path.Combine(mirrorPath, $"rfc{rfcNumber}.txt");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"RFC file not found at '{filePath}'.", filePath);
        }

        var sourceFile = new RfcSourceFile(filePath, rfcNumber);
        await IndexFileAsync(sourceFile, mirrorPath, force, cachedHashes: null, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> GetIndexedCountAsync(CancellationToken cancellationToken) =>
        repository.GetIndexedCountAsync(cancellationToken);

    private async Task IndexFileAsync(
        RfcSourceFile sourceFile,
        string mirrorPath,
        bool force,
        IReadOnlyDictionary<int, string>? cachedHashes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Read file bytes once — used for both SHA-256 and parsing (avoids a second file read)
        byte[] fileBytes = await File.ReadAllBytesAsync(sourceFile.Path, cancellationToken).ConfigureAwait(false);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(fileBytes)).ToUpperInvariant();

        if (!force)
        {
            string? indexedHash = cachedHashes is not null
                ? cachedHashes.GetValueOrDefault(sourceFile.RfcNumber)
                : await repository.GetIndexedRfcHashAsync(sourceFile.RfcNumber, cancellationToken).ConfigureAwait(false);

            if (string.Equals(indexedHash, sourceSha256, StringComparison.Ordinal))
            {
                logger.LogDebug("Skipping unchanged RFC {RfcNumber}", sourceFile.RfcNumber);
                return;
            }
        }

        logger.LogInformation("Indexing RFC {RfcNumber}...", sourceFile.RfcNumber);

        // Decode text from already-read bytes; no second file read needed
        string rawText = Encoding.UTF8.GetString(fileBytes);
        RfcDocument document = parser.ParseContent(rawText, Path.GetFileName(sourceFile.Path));

        string relativePath = Path.GetRelativePath(mirrorPath, sourceFile.Path);
        IReadOnlyList<string> sectionTexts = document.Sections.Select(section => section.Text).ToArray();
        IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
            sectionTexts,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RfcSection> sections = document.Sections
            .Select((section, sectionIndex) => section with
            {
                SourcePath = sourceFile.Path,
                SourceSha256 = sourceSha256,
                Embedding = embeddings[sectionIndex]
            })
            .ToArray();

        await StoreDocumentAsync(
            document,
            sections,
            relativePath,
            sourceSha256,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreDocumentAsync(
        RfcDocument document,
        IReadOnlyList<RfcSection> sections,
        string sourcePath,
        string sourceSha256,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await repository.DeleteByRfcNumberAsync(
                        connection,
                        transaction,
                        document.Metadata.Number,
                        cancellationToken).ConfigureAwait(false);

                    await repository.InsertSectionsAsync(
                        connection,
                        transaction,
                        sections,
                        cancellationToken).ConfigureAwait(false);

                    await repository.InsertAbnfBlocksAsync(
                        connection,
                        transaction,
                        document.AbnfBlocks,
                        cancellationToken).ConfigureAwait(false);

                    await repository.InsertNormativeOccurrencesAsync(
                        connection,
                        transaction,
                        document.NormativeOccurrences,
                        cancellationToken).ConfigureAwait(false);

                    await repository.UpsertIndexedRfcAsync(
                        connection,
                        transaction,
                        document.Metadata.Number,
                        sourcePath,
                        sourceSha256,
                        document.Metadata.Title,
                        sections.Count,
                        document.Metadata.Updates,
                        document.Metadata.Obsoletes,
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private static string ResolveMirrorPath(string mirrorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mirrorPath);

        if (string.Equals(mirrorPath, "~", StringComparison.Ordinal))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (mirrorPath.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                mirrorPath[2..]);
        }

        return mirrorPath;
    }

    private static RfcSourceFile? TryCreateSourceFile(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Length <= 3 || !fileName.StartsWith("rfc", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(fileName[3..], out int rfcNumber)
            ? new RfcSourceFile(path, rfcNumber)
            : null;
    }

    private sealed record class RfcSourceFile(string Path, int RfcNumber);
}
