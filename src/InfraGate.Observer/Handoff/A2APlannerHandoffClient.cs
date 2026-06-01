using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;

namespace InfraGate.Observer.Handoff;

internal sealed class A2APlannerHandoffClient(A2AAgent agent) : IPlannerHandoffClient
{
    public async Task SendAsync(
        string contextId,
        AnomalyHandoffBatch batch,
        CancellationToken cancellationToken)
    {
        var session = await agent.CreateSessionAsync(contextId).ConfigureAwait(false);
        string json = JsonSerializer.Serialize(batch);
        await agent.RunAsync(
            json,
            session,
            options: new AgentRunOptions { AllowBackgroundResponses = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
