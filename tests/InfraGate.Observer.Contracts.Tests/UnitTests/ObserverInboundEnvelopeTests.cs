using System.Text.Json;
using InfraGate.Observer.Contracts;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class ObserverInboundEnvelopeTests
{
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
    public void ObserverInboundIntents_Constants_HaveExpectedValues()
    {
        Assert.Equal("tool-request", ObserverInboundIntents.ToolRequest);
    }
}
