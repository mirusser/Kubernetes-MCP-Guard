using System.Text.Json;
using A2A;
using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using Microsoft.Agents.AI.A2A;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class A2AExecutorDispatchClientTests
{
    [Fact]
    public async Task DispatchAsync_ContextId_SendsPlanSynchronouslyAndReturnsOutcome()
    {
        var a2aClient = new CapturingA2AClient();
#pragma warning disable MEAI001 // A2AAgent is in the accepted experimental Agent Framework package.
        var client = new A2AExecutorDispatchClient(new A2AAgent(a2aClient), Microsoft.Extensions.Logging.Abstractions.NullLogger<A2AExecutorDispatchClient>.Instance);
#pragma warning restore MEAI001

        var result = await client.DispatchAsync("anomaly-1", "plan-1", CancellationToken.None);

        Assert.NotNull(a2aClient.Request);
        Assert.Equal("anomaly-1", a2aClient.Request.Message.ContextId);
        Assert.False(a2aClient.Request.Configuration!.ReturnImmediately);
        Assert.Equal("plan-1", Assert.Single(a2aClient.Request.Message.Parts).Text);
        Assert.Equal(ExecutorDispatchStatuses.Applied, result.Status);
    }

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
                Message = new Message
                {
                    MessageId = "response-1",
                    Role = Role.Agent,
                    ContextId = request.Message.ContextId,
                    Parts =
                    [
                        new Part
                        {
                            Text = JsonSerializer.Serialize(
                                ExecutorDispatchResult.Applied("Plan applied.")),
                        },
                    ],
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
