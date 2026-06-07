using Dapper;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Indexing;

/// <summary>
/// Write-side data access for RFC RAG indexing operations.
/// Manages inserts, deletes, and upserts for indexed RFC content.
/// </summary>
public sealed class IndexingRepository
{
    private readonly NpgsqlDataSource dataSource;

    public IndexingRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    public async Task InsertSectionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<RfcSection> sections,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sections);

        foreach (RfcSection section in sections)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into rfc_rag.rfc_sections
                    (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
                values
                    (@Id, @RfcNumber, @Title, @Section, @Heading, @Text, @SourcePath, @Url, @SourceSha256, cast(@Embedding as vector))
                """,
                section,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task InsertAbnfBlocksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<RfcAbnfBlock> blocks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(blocks);

        foreach (RfcAbnfBlock block in blocks)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into rfc_rag.rfc_abnf_blocks
                    (id, section_id, rfc_number, section, abnf_text, rule_names)
                values
                    (@Id, @SectionId, @RfcNumber, @Section, @AbnfText, @RuleNames)
                """,
                block,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task InsertNormativeOccurrencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<NormativeOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(occurrences);

        foreach (NormativeOccurrence occurrence in occurrences)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into rfc_rag.normative_occurrences
                    (id, section_id, rfc_number, keyword, line_offset)
                values
                    (@Id, @SectionId, @RfcNumber, @Keyword, @LineOffset)
                """,
                occurrence,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task DeleteByRfcNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from rfc_rag.normative_occurrences where rfc_number = @RfcNumber;
            delete from rfc_rag.rfc_abnf_blocks where rfc_number = @RfcNumber;
            delete from rfc_rag.rfc_sections where rfc_number = @RfcNumber;
            delete from rfc_rag.indexed_rfcs where rfc_number = @RfcNumber;
            """,
            new { RfcNumber = rfcNumber },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task UpsertIndexedRfcAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int rfcNumber,
        string sourcePath,
        string sourceSha256,
        string title,
        int sectionCount,
        int[] updates,
        int[] obsoletes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into rfc_rag.indexed_rfcs
                (rfc_number, source_path, source_sha256, title, section_count, updates, obsoletes, indexed_at_utc)
            values
                (@RfcNumber, @SourcePath, @SourceSha256, @Title, @SectionCount, @Updates, @Obsoletes, now())
            on conflict (rfc_number) do update set
                source_path = excluded.source_path,
                source_sha256 = excluded.source_sha256,
                title = excluded.title,
                section_count = excluded.section_count,
                updates = excluded.updates,
                obsoletes = excluded.obsoletes,
                indexed_at_utc = now()
            """,
            new
            {
                RfcNumber = rfcNumber,
                SourcePath = sourcePath,
                SourceSha256 = sourceSha256,
                Title = title,
                SectionCount = sectionCount,
                Updates = updates,
                Obsoletes = obsoletes
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the stored SHA256 hash for an indexed RFC (used for incremental skip detection).
    /// Opens and disposes its own connection via the injected data source.
    /// </summary>
    public async Task<string?> GetIndexedRfcHashAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                select source_sha256
                from rfc_rag.indexed_rfcs
                where rfc_number = @RfcNumber
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the total count of indexed RFCs.
    /// Opens and disposes its own connection via the injected data source.
    /// </summary>
    public async Task<int> GetIndexedCountAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from rfc_rag.indexed_rfcs",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
}
