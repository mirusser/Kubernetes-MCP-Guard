namespace InfraGate.Executor.Watch;

internal interface IExecutorDedupeStore
{
    bool TryTrack(string planId);
    void Remove(string planId);
}
