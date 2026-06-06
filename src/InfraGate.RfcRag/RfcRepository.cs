namespace InfraGate.RfcRag;

public sealed class RfcRepository
{
    private const string SectionProjection = """
        id as "Id",
        rfc_number as "RfcNumber",
        title as "Title",
        section as "Section",
        heading as "Heading",
        text as "Text",
        source_path as "SourcePath",
        url as "Url",
        source_sha256 as "SourceSha256",
        array[]::real[] as "Embedding"
        """;

    private const string SearchResultProjection = """
        rfc_sections.id as "Id",
        rfc_sections.rfc_number as "RfcNumber",
        rfc_sections.title as "Title",
        rfc_sections.section as "Section",
        rfc_sections.heading as "Heading",
        left(rfc_sections.text, 500) as "Excerpt",
        rfc_sections.source_path as "SourcePath",
        rfc_sections.url as "Url"
        """;

    public async Task InsertSectionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<RfcSection> sections,
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into rfc_rag.indexed_rfcs
                (rfc_number, source_path, source_sha256, title, section_count, indexed_at_utc)
            values
                (@RfcNumber, @SourcePath, @SourceSha256, @Title, @SectionCount, now())
            on conflict (rfc_number) do update set
                source_path = excluded.source_path,
                source_sha256 = excluded.source_sha256,
                title = excluded.title,
                section_count = excluded.section_count,
                indexed_at_utc = now()
            """,
            new
            {
                RfcNumber = rfcNumber,
                SourcePath = sourcePath,
                SourceSha256 = sourceSha256,
                Title = title,
                SectionCount = sectionCount
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task<string?> GetIndexedRfcHashAsync(
        NpgsqlConnection connection,
        int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select source_sha256
            from rfc_rag.indexed_rfcs
            where rfc_number = @RfcNumber
            """,
            new { RfcNumber = rfcNumber },
            cancellationToken: cancellationToken));
    }

    public async Task<int> GetIndexedCountAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from rfc_rag.indexed_rfcs",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchLexicalAsync(
        NpgsqlDataSource dataSource,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select
                    {{SearchResultProjection}},
                    ts_rank(search_vector, plainto_tsquery('english', @Query))::float8 as "Score"
                from rfc_rag.rfc_sections
                where plainto_tsquery('english', @Query) @@ search_vector
                order by "Score" desc, rfc_number, section
                limit @Limit
                """,
                new { Query = query, Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchVectorAsync(
        NpgsqlDataSource dataSource,
        float[] embedding,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(embedding);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select
                    {{SearchResultProjection}},
                    (1 / (1 + (embedding <=> cast(@Embedding as vector))))::float8 as "Score"
                from rfc_rag.rfc_sections
                where embedding is not null
                order by embedding <=> cast(@Embedding as vector), rfc_number, section
                limit @Limit
                """,
                new { Embedding = embedding, Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchHybridAsync(
        NpgsqlDataSource dataSource,
        string query,
        float[] embedding,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(embedding);

        int normalizedLimit = NormalizeLimit(limit);
        int candidateLimit = normalizedLimit * 4;
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                with lexical as (
                    select id, row_number() over (order by ts_rank(search_vector, plainto_tsquery('english', @Query)) desc) as rank
                    from rfc_rag.rfc_sections
                    where plainto_tsquery('english', @Query) @@ search_vector
                    limit @CandidateLimit
                ),
                vector as (
                    select id, row_number() over (order by embedding <=> cast(@Embedding as vector)) as rank
                    from rfc_rag.rfc_sections
                    where embedding is not null
                    limit @CandidateLimit
                ),
                fused as (
                    select
                        coalesce(lexical.id, vector.id) as id,
                        (coalesce(1.0 / (60 + lexical.rank), 0) + coalesce(1.0 / (60 + vector.rank), 0))::float8 as score
                    from lexical
                    full join vector on lexical.id = vector.id
                )
                select
                    {{SearchResultProjection}},
                    fused.score as "Score"
                from fused
                join rfc_rag.rfc_sections on rfc_sections.id = fused.id
                order by fused.score desc, rfc_number, section
                limit @Limit
                """,
                new
                {
                    Query = query,
                    Embedding = embedding,
                    CandidateLimit = candidateLimit,
                    Limit = normalizedLimit
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<RfcSection?> GetSectionAsync(
        NpgsqlDataSource dataSource,
        int rfcNumber,
        string section,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber and section = @Section
                """,
                new { RfcNumber = rfcNumber, Section = section },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RfcSection>> GetRfcAsync(
        NpgsqlDataSource dataSource,
        int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber
                order by section
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        NpgsqlDataSource dataSource,
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select distinct on (rfc_sections.id)
                    {{SearchResultProjection}},
                    (1.0 / (1 + occurrences.line_offset))::float8 as "Score"
                from rfc_rag.normative_occurrences occurrences
                join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = occurrences.section_id
                where occurrences.keyword = upper(@Keyword)
                  and (cast(@RfcNumbers as int[]) is null or occurrences.rfc_number = any(cast(@RfcNumbers as int[])))
                order by rfc_sections.id, occurrences.line_offset
                limit @Limit
                """,
                new { Keyword = keyword, RfcNumbers = rfcNumbers, Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        NpgsqlDataSource dataSource,
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select distinct on (rfc_sections.id)
                    {{SearchResultProjection}},
                    ts_rank(blocks.search_vector, plainto_tsquery('english', @Query))::float8 as "Score"
                from rfc_rag.rfc_abnf_blocks blocks
                join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = blocks.section_id
                where plainto_tsquery('english', @Query) @@ blocks.search_vector
                  and (cast(@RfcNumbers as int[]) is null or blocks.rfc_number = any(cast(@RfcNumbers as int[])))
                order by rfc_sections.id, "Score" desc
                limit @Limit
                """,
                new { Query = query, RfcNumbers = rfcNumbers, Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    public async Task<string> GetStatsAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleAsync<string>(new CommandDefinition(
                """
                select json_build_object(
                    'indexedRfcs', (select count(*) from rfc_rag.indexed_rfcs),
                    'sections', (select count(*) from rfc_rag.rfc_sections),
                    'abnfBlocks', (select count(*) from rfc_rag.rfc_abnf_blocks),
                    'normativeOccurrences', (select count(*) from rfc_rag.normative_occurrences),
                    'lastIndexedAtUtc', (select max(indexed_at_utc) from rfc_rag.indexed_rfcs)
                )::text
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, 100);
}
