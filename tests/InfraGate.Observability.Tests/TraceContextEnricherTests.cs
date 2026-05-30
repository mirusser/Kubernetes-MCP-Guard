using System.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using InfraGate.Observability;

namespace InfraGate.Observability.Tests;

public sealed class TraceContextEnricherTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> events = [];
        public IReadOnlyList<LogEvent> Events => events;
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    [Fact]
    public void Enrich_WithCurrentActivity_AddsTraceIdAndSpanId()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<TraceContextEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        using var source = new ActivitySource("test-enrich");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = source.StartActivity("test-span"))
        {
            Assert.NotNull(activity);
            logger.Information("test");
        }

        Assert.Single(sink.Events);
        var evt = sink.Events[0];
        Assert.True(evt.Properties.ContainsKey("TraceId"), "TraceId missing");
        Assert.True(evt.Properties.ContainsKey("SpanId"), "SpanId missing");
    }

    [Fact]
    public void Enrich_WithoutCurrentActivity_AddsNoTraceProperties()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<TraceContextEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Ensure no current activity
        Assert.Null(Activity.Current);

        logger.Information("test");

        Assert.Single(sink.Events);
        var evt = sink.Events[0];
        Assert.False(evt.Properties.ContainsKey("TraceId"), "TraceId should be absent");
        Assert.False(evt.Properties.ContainsKey("SpanId"), "SpanId should be absent");
    }
}
