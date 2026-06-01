using System.Text.Json;
using A2A;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.Planner.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Planner.IntegrationTests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresTaskStoreTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(TaskState.Submitted)]
    [InlineData(TaskState.Working)]
    [InlineData(TaskState.AuthRequired)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Rejected)]
    public async Task SaveTaskAsync_ThenGetTaskAsync_RoundTripsState(TaskState state)
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        var task = CreateTask("task-1", "ctx-1", state);

        await store.SaveTaskAsync(task.Id, task, CancellationToken.None);
        var loaded = await store.GetTaskAsync(task.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("task-1", loaded!.Id);
        Assert.Equal("ctx-1", loaded.ContextId);
        Assert.Equal(state, loaded.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownTaskId_ReturnsNull()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);

        var loaded = await store.GetTaskAsync("does-not-exist", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveTaskAsync_SameTaskIdTwice_UpsertsLatestStateAsSingleRow()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        await store.SaveTaskAsync("task-1", CreateTask("task-1", "ctx-1", TaskState.Working), CancellationToken.None);

        await store.SaveTaskAsync("task-1", CreateTask("task-1", "ctx-1", TaskState.AuthRequired), CancellationToken.None);

        var loaded = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.AuthRequired, loaded!.Status.State);
        var listed = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" }, CancellationToken.None);
        Assert.Equal(1, listed.TotalSize);
    }

    [Fact]
    public async Task TryCreateTaskAsync_SameContextIdTwice_CreatesOnlyFirstTask()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);

        bool firstCreated = await store.TryCreateTaskAsync(
            "task-1",
            CreateTask("task-1", "ctx-1", TaskState.Submitted),
            CancellationToken.None);
        bool secondCreated = await store.TryCreateTaskAsync(
            "task-2",
            CreateTask("task-2", "ctx-1", TaskState.Submitted),
            CancellationToken.None);

        Assert.True(firstCreated);
        Assert.False(secondCreated);
        var listed = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" }, CancellationToken.None);
        Assert.Equal("task-1", Assert.Single(listed.Tasks).Id);
    }

    [Fact]
    public async Task TryCreateTaskAsync_ConcurrentSameContextId_CreatesOneTask()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);

        var attempts = Enumerable.Range(1, 4)
            .Select(index =>
                store.TryCreateTaskAsync(
                    $"task-{index}",
                    CreateTask($"task-{index}", "ctx-1", TaskState.Submitted),
                    CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, static created => created);
        var listed = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" }, CancellationToken.None);
        Assert.Single(listed.Tasks);
    }

    [Fact]
    public async Task DeleteTaskAsync_ExistingTask_RemovesTask()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        await store.SaveTaskAsync("task-1", CreateTask("task-1", "ctx-1", TaskState.Working), CancellationToken.None);

        await store.DeleteTaskAsync("task-1", CancellationToken.None);

        Assert.Null(await store.GetTaskAsync("task-1", CancellationToken.None));
    }

    [Fact]
    public async Task ListTasksAsync_FilterByContextId_ReturnsOnlyMatching()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        await store.SaveTaskAsync("task-a", CreateTask("task-a", "ctx-a", TaskState.Working), CancellationToken.None);
        await store.SaveTaskAsync("task-b", CreateTask("task-b", "ctx-b", TaskState.Working), CancellationToken.None);

        var listed = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-a" }, CancellationToken.None);

        Assert.Equal(1, listed.TotalSize);
        Assert.Equal("task-a", Assert.Single(listed.Tasks).Id);
    }

    [Fact]
    public async Task ListTasksAsync_FilterByStatus_ReturnsOnlyMatching()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        await store.SaveTaskAsync("task-waiting", CreateTask("task-waiting", "ctx-1", TaskState.AuthRequired), CancellationToken.None);
        await store.SaveTaskAsync("task-working", CreateTask("task-working", "ctx-2", TaskState.Working), CancellationToken.None);

        var listed = await store.ListTasksAsync(
            new ListTasksRequest { Status = TaskState.AuthRequired },
            CancellationToken.None);

        Assert.Equal("task-waiting", Assert.Single(listed.Tasks).Id);
    }

    [Fact]
    public async Task SaveTaskAsync_TaskWithArtifactAndMetadata_RoundTripsFidelity()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var store = await CreateStoreAsync(dataSource);
        var task = CreateTask("task-1", "ctx-1", TaskState.Working);
        task.Artifacts =
        [
            new Artifact
            {
                ArtifactId = "plan-ref",
                Name = "plan",
                Parts = [new Part { Text = "plan-123" }],
            },
        ];
        task.Metadata = new Dictionary<string, JsonElement>
        {
            ["anomalyId"] = JsonSerializer.SerializeToElement("anomaly-123"),
        };

        await store.SaveTaskAsync(task.Id, task, CancellationToken.None);
        var loaded = await store.GetTaskAsync(task.Id, CancellationToken.None);

        var artifact = Assert.Single(loaded!.Artifacts!);
        Assert.Equal("plan-ref", artifact.ArtifactId);
        Assert.Equal("plan-123", Assert.Single(artifact.Parts).Text);
        Assert.Equal("anomaly-123", loaded.Metadata!["anomalyId"].GetString());
    }

    private static AgentTask CreateTask(string id, string contextId, TaskState state) =>
        new()
        {
            Id = id,
            ContextId = contextId,
            Status = new TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        };

    private static async Task<PostgresTaskStore> CreateStoreAsync(NpgsqlDataSource dataSource)
    {
        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource,
            PlannerTaskStoreConventions.Schema,
            ResolveMigrationsDirectory(),
            CancellationToken.None);
        return new PostgresTaskStore(dataSource);
    }

    private static string ResolveMigrationsDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Tasks", "Migrations");
        if (!Directory.Exists(dir))
        {
            var sourcePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "../../../../src/InfraGate.Planner/Tasks/Migrations"));
            if (Directory.Exists(sourcePath))
            {
                dir = sourcePath;
            }
        }

        return dir;
    }
}
