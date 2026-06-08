using NpgsqlTypes;

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

        if (sections.Count == 0)
        {
            return;
        }

        var batch = new NpgsqlBatch(connection, transaction);
        await using (batch.ConfigureAwait(false))
        {
            foreach (RfcSection section in sections)
            {
                var cmd = new NpgsqlBatchCommand(
                    """
                    insert into rfc_rag.rfc_sections
                        (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
                    values
                        (@Id, @RfcNumber, @Title, @Section, @Heading, @Text, @SourcePath, @Url, @SourceSha256, cast(@Embedding as vector))
                    """);
                cmd.Parameters.AddWithValue("Id", section.Id);
                cmd.Parameters.AddWithValue("RfcNumber", section.RfcNumber);
                cmd.Parameters.AddWithValue("Title", section.Title);
                cmd.Parameters.AddWithValue("Section", section.Section);
                cmd.Parameters.AddWithValue("Heading", (object?)section.Heading ?? DBNull.Value);
                cmd.Parameters.AddWithValue("Text", section.Text);
                cmd.Parameters.AddWithValue("SourcePath", section.SourcePath);
                cmd.Parameters.AddWithValue("Url", section.Url);
                cmd.Parameters.AddWithValue("SourceSha256", section.SourceSha256);
                cmd.Parameters.Add(new NpgsqlParameter("Embedding", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = (object?)section.Embedding ?? DBNull.Value });
                batch.BatchCommands.Add(cmd);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        if (blocks.Count == 0)
        {
            return;
        }

        var batch = new NpgsqlBatch(connection, transaction);
        await using (batch.ConfigureAwait(false))
        {
            foreach (RfcAbnfBlock block in blocks)
            {
                var cmd = new NpgsqlBatchCommand(
                    """
                    insert into rfc_rag.rfc_abnf_blocks
                        (id, section_id, rfc_number, section, abnf_text, rule_names)
                    values
                        (@Id, @SectionId, @RfcNumber, @Section, @AbnfText, @RuleNames)
                    """);
                cmd.Parameters.AddWithValue("Id", block.Id);
                cmd.Parameters.AddWithValue("SectionId", block.SectionId);
                cmd.Parameters.AddWithValue("RfcNumber", block.RfcNumber);
                cmd.Parameters.AddWithValue("Section", block.Section);
                cmd.Parameters.AddWithValue("AbnfText", block.AbnfText);
                cmd.Parameters.AddWithValue("RuleNames", block.RuleNames);
                batch.BatchCommands.Add(cmd);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        if (occurrences.Count == 0)
        {
            return;
        }

        var batch = new NpgsqlBatch(connection, transaction);
        await using (batch.ConfigureAwait(false))
        {
            foreach (NormativeOccurrence occurrence in occurrences)
            {
                var cmd = new NpgsqlBatchCommand(
                    """
                    insert into rfc_rag.normative_occurrences
                        (id, section_id, rfc_number, keyword, line_offset)
                    values
                        (@Id, @SectionId, @RfcNumber, @Keyword, @LineOffset)
                    """);
                cmd.Parameters.AddWithValue("Id", occurrence.Id);
                cmd.Parameters.AddWithValue("SectionId", occurrence.SectionId);
                cmd.Parameters.AddWithValue("RfcNumber", occurrence.RfcNumber);
                cmd.Parameters.AddWithValue("Keyword", occurrence.Keyword);
                cmd.Parameters.AddWithValue("LineOffset", occurrence.LineOffset);
                batch.BatchCommands.Add(cmd);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        string? date,
        string? category,
        string[] authors,
        string? issn,
        string grammarStyle,
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
                (rfc_number, source_path, source_sha256, title, section_count,
                 updates, obsoletes, rfc_date, category, authors, issn, grammar_style, indexed_at_utc)
            values
                (@RfcNumber, @SourcePath, @SourceSha256, @Title, @SectionCount,
                 @Updates, @Obsoletes, @Date, @Category, @Authors, @Issn, @GrammarStyle, now())
            on conflict (rfc_number) do update set
                source_path = excluded.source_path,
                source_sha256 = excluded.source_sha256,
                title = excluded.title,
                section_count = excluded.section_count,
                updates = excluded.updates,
                obsoletes = excluded.obsoletes,
                rfc_date = excluded.rfc_date,
                category = excluded.category,
                authors = excluded.authors,
                issn = excluded.issn,
                grammar_style = excluded.grammar_style,
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
                Obsoletes = obsoletes,
                Date = date,
                Category = category,
                Authors = authors,
                Issn = issn,
                GrammarStyle = grammarStyle
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads all indexed RFC hashes in a single query for bulk skip detection during IndexAllAsync.
    /// </summary>
    public async Task<Dictionary<int, string>> GetAllIndexedHashesAsync(CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<(int, string)>(new CommandDefinition(
                "select rfc_number, source_sha256 from rfc_rag.indexed_rfcs",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows.ToDictionary(r => r.Item1, r => r.Item2);
        }
    }

    /// <summary>
    /// Gets the stored SHA256 hash for a single indexed RFC.
    /// Used by IndexSingleAsync for per-file incremental detection.
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
