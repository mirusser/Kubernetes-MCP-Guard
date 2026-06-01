using A2A;
using Dapper;
using Npgsql;

namespace InfraGate.Planner.Tasks;

/// <summary>
/// Durable <see cref="ITaskStore"/> backed by PostgreSQL. The Planner owns one A2A task per
/// remediation (keyed by <c>contextId = AnomalyId</c>); persisting it here lets the Planner
/// reconcile in-flight tasks after a restart. The full <see cref="AgentTask"/> is stored as JSON
/// for round-trip fidelity, with context/state/timestamp broken out for <see cref="ListTasksAsync"/>.
/// </summary>
internal sealed class PostgresTaskStore(NpgsqlDataSource dataSource) : IPlannerTaskStore
{
    private const int DefaultPageSize = 50;

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            string? taskJson = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                $"""
                select task_json
                from {PlannerTaskStoreConventions.QualifiedTable}
                where task_id = @TaskId
                """,
                new { TaskId = taskId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return taskJson is null ? null : DeserializeTask(taskJson);
        }
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(task);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                insert into {PlannerTaskStoreConventions.QualifiedTable} (
                    task_id,
                    context_id,
                    status_state,
                    status_timestamp,
                    task_json,
                    updated_at_utc)
                values (
                    @TaskId,
                    @ContextId,
                    @StatusState,
                    @StatusTimestamp,
                    @TaskJson,
                    now())
                on conflict (task_id) do update set
                    context_id = excluded.context_id,
                    status_state = excluded.status_state,
                    status_timestamp = excluded.status_timestamp,
                    task_json = excluded.task_json,
                    updated_at_utc = now()
                """,
                new
                {
                    TaskId = taskId,
                    task.ContextId,
                    StatusState = SerializeState(task.Status.State),
                    StatusTimestamp = task.Status.Timestamp,
                    TaskJson = SerializeTask(task)
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<bool> TryCreateTaskAsync(
        string taskId,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(task);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            int? inserted = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"""
                insert into {PlannerTaskStoreConventions.QualifiedTable} (
                    task_id,
                    context_id,
                    status_state,
                    status_timestamp,
                    task_json,
                    updated_at_utc)
                values (
                    @TaskId,
                    @ContextId,
                    @StatusState,
                    @StatusTimestamp,
                    @TaskJson,
                    now())
                on conflict (context_id) do nothing
                returning 1
                """,
                new
                {
                    TaskId = taskId,
                    task.ContextId,
                    StatusState = SerializeState(task.Status.State),
                    StatusTimestamp = task.Status.Timestamp,
                    TaskJson = SerializeTask(task)
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return inserted is not null;
        }
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                delete from {PlannerTaskStoreConventions.QualifiedTable}
                where task_id = @TaskId
                """,
                new { TaskId = taskId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<ListTasksResponse> ListTasksAsync(
        ListTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int startIndex = ParsePageToken(request.PageToken);
        int pageSize = request.PageSize ?? DefaultPageSize;

        var parameters = new DynamicParameters();
        string whereClause = BuildWhereClause(request, parameters);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Justification: The whereClause is built from ListTasksRequest properties via
            // BuildWhereClause using only Dapper parameterized placeholders (@ContextId, @StatusState,
            // @StatusTimestampAfter). QualifiedTable is a compile-time const string. No user input
            // is concatenated into the SQL text.
            int totalSize = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"select count(*) from {PlannerTaskStoreConventions.QualifiedTable} {whereClause}",
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            parameters.Add("Limit", pageSize);
            parameters.Add("Offset", startIndex);
            // Justification: Same safe parameterized pattern — whereClause uses only Dapper
            // placeholders, QualifiedTable is a const, and user input goes through DynamicParameters.
            var rows = await connection.QueryAsync<string>(new CommandDefinition(
                $"""
                select task_json
                from {PlannerTaskStoreConventions.QualifiedTable}
                {whereClause}
                order by status_timestamp desc nulls last, task_id
                limit @Limit offset @Offset
                """,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var page = rows.Select(DeserializeTask).ToList();
            TrimHistory(page, request.HistoryLength);
            if (request.IncludeArtifacts is not true)
            {
                foreach (var task in page)
                {
                    task.Artifacts = null;
                }
            }

            int nextIndex = startIndex + page.Count;
            return new ListTasksResponse
            {
                Tasks = page,
                NextPageToken = nextIndex < totalSize
                    ? nextIndex.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                PageSize = page.Count,
                TotalSize = totalSize,
            };
        }
    }

    private static string BuildWhereClause(ListTasksRequest request, DynamicParameters parameters)
    {
        var filters = new List<string>();

        if (!string.IsNullOrEmpty(request.ContextId))
        {
            filters.Add("context_id = @ContextId");
            parameters.Add("ContextId", request.ContextId);
        }

        if (request.Status is { } status)
        {
            filters.Add("status_state = @StatusState");
            parameters.Add("StatusState", SerializeState(status));
        }

        if (request.StatusTimestampAfter is { } statusTimestampAfter)
        {
            filters.Add("status_timestamp is not null and status_timestamp > @StatusTimestampAfter");
            parameters.Add("StatusTimestampAfter", statusTimestampAfter);
        }

        return filters.Count == 0 ? string.Empty : "where " + string.Join(" and ", filters);
    }

    private static int ParsePageToken(string? pageToken)
    {
        if (string.IsNullOrEmpty(pageToken))
        {
            return 0;
        }

        if (!int.TryParse(pageToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset) || offset < 0)
        {
            throw new A2AException($"Invalid pageToken: {pageToken}", A2AErrorCode.InvalidParams);
        }

        return offset;
    }

    private static void TrimHistory(IReadOnlyList<AgentTask> tasks, int? historyLength)
    {
        if (historyLength is not { } length)
        {
            return;
        }

        foreach (var task in tasks)
        {
            if (length == 0)
            {
                task.History = null;
            }
            else if (task.History is { Count: > 0 } history)
            {
                task.History = history.Skip(Math.Max(0, history.Count - length)).ToList();
            }
        }
    }

    private static string SerializeTask(AgentTask task) =>
        JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);

    private static AgentTask DeserializeTask(string taskJson) =>
        JsonSerializer.Deserialize<AgentTask>(taskJson, A2AJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException("Stored A2A task could not be deserialized.");

    private static string SerializeState(TaskState state) =>
        JsonSerializer.Serialize(state, A2AJsonUtilities.DefaultOptions).Trim('"');
}
