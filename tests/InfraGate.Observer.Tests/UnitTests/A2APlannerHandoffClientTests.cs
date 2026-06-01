using A2A;
using InfraGate.Observer.Handoff;
using Microsoft.Agents.AI.A2A;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class A2APlannerHandoffClientTests
{
    [Fact]
    public async Task SendAsync_ContextId_RequestsImmediateTaskHandle()
    {
        var a2aClient = new CapturingA2AClient();
#pragma warning disable MEAI001 // A2AAgent is in the accepted experimental Agent Framework package.
        var client = new A2APlannerHandoffClient(new A2AAgent(a2aClient));
#pragma warning restore MEAI001
        var batch = CreateBatch();

        await client.SendAsync("anomaly-1", batch, CancellationToken.None);

        Assert.NotNull(a2aClient.Request);
        Assert.Equal("anomaly-1", a2aClient.Request.Message.ContextId);
        Assert.True(a2aClient.Request.Configuration!.ReturnImmediately);
        var sentBatch = JsonSerializer.Deserialize<AnomalyHandoffBatch>(
            Assert.Single(a2aClient.Request.Message.Parts).Text!);
        Assert.Equal("anomaly-1", Assert.Single(sentBatch!.Reports).AnomalyId);
    }

    private static AnomalyHandoffBatch CreateBatch() => new()
    {
        CycleId = "cycle-1",
        EmittedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        Reports =
        [
            new AnomalyReport
            {
                AnomalyId = "anomaly-1",
                CycleId = "cycle-1",
                DetectedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                Kind = AnomalyKind.DeploymentUnavailable,
                Target = new ResourceRef
                {
                    ApiVersion = "apps/v1",
                    Kind = "Deployment",
                    Namespace = "default",
                    Name = "nginx",
                },
                Severity = Severity.High,
                Status = AnomalyStatus.Active,
                Summary = "Deployment is unavailable",
                Evidence = [],
                Annotations = new Dictionary<string, string>(),
            },
        ],
    };

    private sealed class CapturingA2AClient : IA2AClient
    {
        public SendMessageRequest? Request { get; private set; }

        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new SendMessageResponse
            {
                Task = new AgentTask
                {
                    Id = "task-1",
                    ContextId = request.Message.ContextId!,
                    Status = new TaskStatus { State = TaskState.Submitted },
                },
            });
        }

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            CreateTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(
            ListTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
