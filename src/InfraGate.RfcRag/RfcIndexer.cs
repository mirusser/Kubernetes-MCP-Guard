using System.Security.Cryptography;

namespace InfraGate.RfcRag;

public sealed class RfcIndexer : IIndexerService
{
    private readonly NpgsqlDataSource dataSource;
    private readonly RfcRepository repository;
    private readonly RfcParser parser;
    private readonly EmbeddingService embeddingService;
    private readonly RfcRagOptions options;
    private readonly ILogger<RfcIndexer> logger;

    public RfcIndexer(
        NpgsqlDataSource dataSource,
        RfcRepository repository,
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

        IReadOnlyList<RfcSourceFile> sourceFiles = Directory
            .EnumerateFiles(mirrorPath, "rfc*.txt", SearchOption.AllDirectories)
            .Select(TryCreateSourceFile)
            .OfType<RfcSourceFile>()
            .OrderBy(file => file.RfcNumber)
            .ToArray();

        logger.LogInformation("Indexing {RfcCount} RFC source files from {MirrorPath}", sourceFiles.Count, mirrorPath);

        for (int index = 0; index < sourceFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RfcSourceFile sourceFile = sourceFiles[index];
            string sourceSha256 = await ComputeSha256Async(sourceFile.Path, cancellationToken).ConfigureAwait(false);

            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                string? indexedHash = await repository.GetIndexedRfcHashAsync(
                    connection,
                    sourceFile.RfcNumber,
                    cancellationToken).ConfigureAwait(false);

                if (string.Equals(indexedHash, sourceSha256, StringComparison.Ordinal))
                {
                    logger.LogDebug("Skipping unchanged RFC {RfcNumber}", sourceFile.RfcNumber);
                    continue;
                }
            }

            logger.LogInformation(
                "Indexing RFC {RfcNumber} ({Current}/{Total})...",
                sourceFile.RfcNumber,
                index + 1,
                sourceFiles.Count);

            RfcDocument document = await parser.ParseAsync(sourceFile.Path, cancellationToken).ConfigureAwait(false);
            string relativePath = Path.GetRelativePath(mirrorPath, sourceFile.Path);
            IReadOnlyList<string> sectionTexts = document.Sections.Select(section => section.Text).ToArray();
            IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
                sectionTexts,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<RfcSection> sections = document.Sections
                .Select((section, sectionIndex) => section with
                {
                    SourcePath = relativePath,
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
    }

    public Task<int> GetIndexedCountAsync(CancellationToken cancellationToken) =>
        repository.GetIndexedCountAsync(dataSource, cancellationToken);

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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToUpperInvariant();
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
