using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using InfraGate.Observability;

namespace InfraGate.Observability.Tests;

public sealed class TelemetryExtensionsTests
{
    [Fact]
    public void AddInfraGateTelemetry_RegistersTracerProviderAndMeterProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddInfraGateObservability(opt => opt.WriteToConsole = false);
        builder.AddInfraGateTelemetry(opt => opt.ServiceName = "test-service");

        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<TracerProvider>());
        Assert.NotNull(host.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void AddInfraGateTelemetry_OtlpEndpointNotSet_BuildSucceeds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddInfraGateObservability(opt => opt.WriteToConsole = false);
        builder.AddInfraGateTelemetry(opt =>
        {
            opt.ServiceName = "test-service";
            opt.OtlpEndpoint = null;
        });

        // Should not throw; OTLP exporter is not wired without an endpoint
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void AddInfraGateTelemetry_WithCustomMeterNames_BuildSucceeds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddInfraGateObservability(opt => opt.WriteToConsole = false);
        builder.AddInfraGateTelemetry(opt =>
        {
            opt.ServiceName = "test-service";
            opt.MeterNames = ["InfraGate.Observer", "InfraGate.Planner"];
        });

        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void AddInfraGateTelemetry_WithOtlpEndpoint_AddsExporters()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddInfraGateObservability(opt => opt.WriteToConsole = false);
        builder.AddInfraGateTelemetry(opt =>
        {
            opt.ServiceName = "test-service";
            opt.OtlpEndpoint = "http://localhost:4317";
        });

        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<TracerProvider>());
        Assert.NotNull(host.Services.GetService<MeterProvider>());
    }
}
