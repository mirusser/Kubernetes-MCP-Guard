using Microsoft.Extensions.AI;

namespace InfraGate.Observer.Llm;

internal interface IChatClientFactory
{
    IChatClient Create();
}
