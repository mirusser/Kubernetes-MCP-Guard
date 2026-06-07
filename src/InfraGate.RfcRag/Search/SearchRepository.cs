using Dapper;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Search;

/// <summary>
/// Read-side data access for RFC search operations.
/// Handles hybrid, lexical, vector, normative, and ABNF searches.
/// </summary>
public sealed class SearchRepository
{
    private readonly NpgsqlDataSource dataSource;

    public SearchRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

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

    private const int MaxLimit = 100;

    /// <summary>
    /// Full-text lexical search using PostgreSQL tsvector.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchLexicalAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        int normalizedLimit = NormalizeLimit(limit);
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
                new { Query = query, Limit = normalizedLimit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Vector similarity search using cosine distance (pgvector).
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchVectorAsync(
        float[] embedding,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        int normalizedLimit = NormalizeLimit(limit);
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
                new { Embedding = embedding, Limit = normalizedLimit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Hybrid search combining vector similarity and full-text lexical search
    /// with reciprocal rank fusion (RRF).
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchHybridAsync(
        string query,
        float[] embedding,
        int limit,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Retrieve a single section of an RFC by number and section identifier.
    /// </summary>
    public async Task<RfcSection?> GetSectionAsync(
        int rfcNumber,
        string section,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Retrieve all sections of an RFC, ordered by section number.
    /// </summary>
    public async Task<IReadOnlyList<RfcSection>> GetRfcAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Search for normative keywords (RFC 2119/8174) across indexed RFCs.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select * from (
                    select distinct on (rfc_sections.id)
                        {{SearchResultProjection}},
                        (1.0 / (1 + occurrences.line_offset))::float8 as "Score"
                    from rfc_rag.normative_occurrences occurrences
                    join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = occurrences.section_id
                    where occurrences.keyword = upper(@Keyword)
                      {{(rfcNumbers is not null ? "and occurrences.rfc_number = any(@RfcNumbers)" : "")}}
                    order by rfc_sections.id, occurrences.line_offset
                ) ranked
                order by "Score" desc, "RfcNumber", "Section"
                limit @Limit
                """,
                new { Keyword = keyword, RfcNumbers = rfcNumbers ?? [], Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Search ABNF grammar definitions by rule name or fragment.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select * from (
                    select distinct on (rfc_sections.id)
                        {{SearchResultProjection}},
                        ts_rank(blocks.search_vector, plainto_tsquery('english', @Query))::float8 as "Score"
                    from rfc_rag.rfc_abnf_blocks blocks
                    join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = blocks.section_id
                    where (plainto_tsquery('english', @Query) @@ blocks.search_vector
                       or blocks.abnf_text ilike '%' || @Query || '%')
                      {{(rfcNumbers is not null ? "and blocks.rfc_number = any(@RfcNumbers)" : "")}}
                    order by rfc_sections.id, "Score" desc
                ) ranked
                order by "Score" desc, "RfcNumber", "Section"
                limit @Limit
                """,
                new { Query = query, RfcNumbers = rfcNumbers ?? [], Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);
}
