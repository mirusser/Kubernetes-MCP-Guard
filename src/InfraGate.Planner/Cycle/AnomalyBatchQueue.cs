namespace InfraGate.Planner.Cycle;

internal sealed class AnomalyBatchQueue
{
    private readonly Channel<AnomalyHandoffBatch> channel = Channel.CreateUnbounded<AnomalyHandoffBatch>();

    public ChannelWriter<AnomalyHandoffBatch> Writer => channel.Writer;

    public ChannelReader<AnomalyHandoffBatch> Reader => channel.Reader;

    public bool TryEnqueue(AnomalyHandoffBatch batch) => channel.Writer.TryWrite(batch);
}
