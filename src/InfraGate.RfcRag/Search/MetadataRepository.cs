using Dapper;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Search;

/// <summary>
/// Read-side data access for RFC metadata operations.
/// Handles metadata retrieval, listing, back-reference lookup, and statistics.
/// </summary>
public sealed class MetadataRepository
{
    private readonly NpgsqlDataSource dataSource;

    public MetadataRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    /// <summary>
    /// Retrieve metadata for a specific RFC (title, updates, obsoletes).
    /// </summary>
    public async Task<RfcMetadata?> GetIndexedRfcMetadataAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title",
                    updates as "Updates",
                    obsoletes as "Obsoletes"
                from rfc_rag.indexed_rfcs
                where rfc_number = @RfcNumber
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// List indexed RFCs with pagination.
    /// </summary>
    public async Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title"
                from rfc_rag.indexed_rfcs
                order by rfc_number
                limit @Limit
                offset @Offset
                """,
                new { Limit = Math.Clamp(limit, 1, 1000), Offset = Math.Max(0, offset) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Find RFCs that reference this RFC number in their updates or obsoletes arrays.
    /// </summary>
    public async Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title"
                from rfc_rag.indexed_rfcs
                where @RfcNumber = any(updates) or @RfcNumber = any(obsoletes)
                order by rfc_number
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Get statistics about the indexed RFC corpus.
    /// </summary>
    public async Task<string> GetStatsAsync(CancellationToken cancellationToken = default)
    {
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
}
