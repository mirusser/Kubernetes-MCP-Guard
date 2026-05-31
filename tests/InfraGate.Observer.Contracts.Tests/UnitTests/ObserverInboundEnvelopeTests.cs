using System.Text.Json;
using InfraGate.Observer.Contracts;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class ObserverInboundEnvelopeTests
{
    [Fact]
    public void Progress_Envelope_RoundTripsJson()
    {
        var envelope = new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = "cycle-123",
            Progress = new PlanProgressPayload { Stage = PlanProgressStage.Analyzing },
        };

        string json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<ObserverInboundEnvelope>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(ObserverInboundIntents.Progress, deserialized.Intent);
        Assert.Equal("cycle-123", deserialized.CycleId);
        Assert.NotNull(deserialized.Progress);
        Assert.Equal(PlanProgressStage.Analyzing, deserialized.Progress.Stage);
        Assert.Null(deserialized.Progress.Detail);
        Assert.Null(deserialized.Progress.ProposalCount);
    }

    [Fact]
    public void PlanProposed_Envelope_PreservesProposalCountAndDetail()
    {
        var envelope = new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = "cycle-456",
            Progress = new PlanProgressPayload
            {
                Stage = PlanProgressStage.PlanProposed,
                Detail = "2 proposals",
                ProposalCount = 2,
            },
        };

        string json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<ObserverInboundEnvelope>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Progress?.ProposalCount);
        Assert.Equal("2 proposals", deserialized.Progress?.Detail);
    }

    [Fact]
    public void ToolRequest_Envelope_RoundTripsJson()
    {
        var envelope = new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = "cycle-789",
            ToolRequest = new ToolRequestPayload { ToolName = "get_k8s_events" },
        };

        string json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<ObserverInboundEnvelope>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(ObserverInboundIntents.ToolRequest, deserialized.Intent);
        Assert.NotNull(deserialized.ToolRequest);
        Assert.Equal("get_k8s_events", deserialized.ToolRequest.ToolName);
        Assert.Null(deserialized.ToolRequest.ArgumentsJson);
    }

    [Fact]
    public void ToolResponse_RoundTripsJson()
    {
        var response = new ToolResponsePayload { IsError = false, ResultJson = "{\"events\":[]}" };

        string json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<ToolResponsePayload>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsError);
        Assert.Equal("{\"events\":[]}", deserialized.ResultJson);
    }

    [Fact]
    public void PlanProgressStage_Constants_HaveExpectedValues()
    {
        Assert.Equal("analyzing", PlanProgressStage.Analyzing);
        Assert.Equal("failed", PlanProgressStage.Failed);
        Assert.Equal("no_action", PlanProgressStage.NoAction);
        Assert.Equal("plan_proposed", PlanProgressStage.PlanProposed);
    }

    [Fact]
    public void ObserverInboundIntents_Constants_HaveExpectedValues()
    {
        Assert.Equal("progress", ObserverInboundIntents.Progress);
        Assert.Equal("tool-request", ObserverInboundIntents.ToolRequest);
    }
}
