using System.Diagnostics.Metrics;
using Dapper;
using InfraGate.Approvals;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

public sealed class PostgresApprovalAccessCodeStore(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null) :
    IApprovalAccessCodeStore
{
    private static readonly Meter Meter = new("InfraGate.Approvals.Postgres", "1.0");
    private static readonly Counter<long> ExpiredCodeCounter =
        Meter.CreateCounter<long>("infragate.gateway.code.expired");

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public async Task<ApprovalAccessCode> GenerateAsync(
        string challengeId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Approval access code TTL must be positive.");
        }

        var expiresAtUtc = time.GetUtcNow().Add(ttl);
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            while (true)
            {
                string code = ApprovalAccessCodeGenerator.Generate();
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.approval_access_codes (
                            code,
                            challenge_id,
                            expires_at_utc,
                            consumed_at_utc)
                        values (
                            @Code,
                            @ChallengeId,
                            @ExpiresAtUtc,
                            null)
                        """,
                        new
                        {
                            Code = code,
                            ChallengeId = challengeId,
                            ExpiresAtUtc = expiresAtUtc
                        },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                    return new ApprovalAccessCode(code, challengeId, expiresAtUtc);
                }
                catch (PostgresException ex) when (string.Equals(
                           ex.SqlState,
                           PostgresErrorCodes.UniqueViolation,
                           StringComparison.Ordinal))
                {
                    // Random access-code collision; retry with a new generated code.
                }
            }
        }
    }

    public async Task<ApprovalAccessCodeConsumeResult> ConsumeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        string? normalized = Normalize(code);
        if (normalized is null)
        {
            return ApprovalAccessCodeConsumeResult.Invalid();
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var row = await ReadAccessCodeForUpdateAsync(
                        connection,
                        transaction,
                        normalized,
                        cancellationToken).ConfigureAwait(false);
                    if (row is null)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return ApprovalAccessCodeConsumeResult.Invalid();
                    }

                    var now = time.GetUtcNow();
                    if (row.ExpiresAtUtc <= now)
                    {
                        ExpiredCodeCounter.Add(1);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return ApprovalAccessCodeConsumeResult.Expired();
                    }

                    if (row.ConsumedAtUtc is not null)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return ApprovalAccessCodeConsumeResult.Consumed();
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update approvals.approval_access_codes
                        set consumed_at_utc = @ConsumedAtUtc
                        where code = @Code
                        """,
                        new
                        {
                            Code = normalized,
                            ConsumedAtUtc = now
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return ApprovalAccessCodeConsumeResult.Success(row.ChallengeId);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private static async Task<AccessCodeReadResult?> ReadAccessCodeForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string code,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                """
                select challenge_id,
                       expires_at_utc,
                       consumed_at_utc
                from approvals.approval_access_codes
                where code = @Code
                for update
                """;
            command.Parameters.AddWithValue("Code", code);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return new AccessCodeReadResult(
                    reader.GetString(0),
                    await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false),
                    await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    private static string? Normalize(string code)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return normalized.Length == ApprovalConventions.AccessCodes.CodeLength &&
               normalized.All(c => ApprovalConventions.AccessCodes.Alphabet.Contains(c, StringComparison.Ordinal))
            ? normalized
            : null;
    }

    private sealed record class AccessCodeReadResult(
        string ChallengeId,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ConsumedAtUtc);
}
