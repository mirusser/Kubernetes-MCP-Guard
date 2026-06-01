namespace InfraGate.Planner.Cycle;

internal sealed class AnomalyBatchQueue
{
    private readonly Channel<PlannerTaskWorkItem> channel = Channel.CreateUnbounded<PlannerTaskWorkItem>();

    public ChannelWriter<PlannerTaskWorkItem> Writer => channel.Writer;

    public ChannelReader<PlannerTaskWorkItem> Reader => channel.Reader;

    public bool TryEnqueue(PlannerTaskWorkItem workItem) => channel.Writer.TryWrite(workItem);
}
