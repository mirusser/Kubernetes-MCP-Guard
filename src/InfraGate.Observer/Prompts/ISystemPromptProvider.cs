namespace InfraGate.Observer.Prompts;

internal interface ISystemPromptProvider
{
    string Get(string namespaceName, int maxToolIterations);
}
