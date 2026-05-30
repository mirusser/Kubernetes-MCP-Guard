using System.Diagnostics;
using Microsoft.Extensions.Logging;
using InfraGate.Observability;

namespace InfraGate.Observability.Tests;

public sealed class SerilogSpanProcessorTests
{
    private sealed class CapturingLogger : ILogger<SerilogSpanProcessor>
    {
        private readonly List<(LogLevel Level, string Message)> entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => entries;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private static Activity CreateActivity(string sourceName, string spanName, params (string Key, string Value)[] tags)
    {
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var activity = source.StartActivity(spanName)!;
        foreach (var (key, value) in tags)
            activity.SetTag(key, value);

        activity.Stop();
        return activity;
    }

    [Fact]
    public void OnEnd_RelevantSourceWithGenAiTags_LogsAtDebugLevel()
    {
        var capturingLogger = new CapturingLogger();
        var processor = new SerilogSpanProcessor(capturingLogger);

        var activity = CreateActivity(
            TelemetryConventions.AgentsAiSourceName,
            "invoke_agent",
            ("gen_ai.operation.name", "invoke_agent"),
            ("gen_ai.request.model", "gpt-4"),
            ("gen_ai.usage.input_tokens", "100"),
            ("gen_ai.usage.output_tokens", "50"));

        processor.OnEnd(activity);

        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Debug, capturingLogger.Entries[0].Level);
    }

    [Fact]
    public void OnEnd_IrrelevantSource_DoesNotLog()
    {
        var capturingLogger = new CapturingLogger();
        var processor = new SerilogSpanProcessor(capturingLogger);

        var activity = CreateActivity("unrelated-source", "some-span");

        processor.OnEnd(activity);

        Assert.Empty(capturingLogger.Entries);
    }

    [Fact]
    public void OnEnd_LongTokenCounts_LogsCorrectly()
    {
        var capturingLogger = new CapturingLogger();
        var processor = new SerilogSpanProcessor(capturingLogger);

        using var source = new ActivitySource(TelemetryConventions.AgentsAiSourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var activity = source.StartActivity("invoke_agent")!;
        activity.SetTag("gen_ai.operation.name", "invoke_agent");
        activity.SetTag("gen_ai.request.model", "gpt-4");
        activity.SetTag("gen_ai.usage.input_tokens", 100L);
        activity.SetTag("gen_ai.usage.output_tokens", 50L);
        activity.Stop();

        processor.OnEnd(activity);

        Assert.Single(capturingLogger.Entries);
    }

    [Fact]
    public void OnEnd_ResponseModelFallback_LogsWhenRequestModelMissing()
    {
        var capturingLogger = new CapturingLogger();
        var processor = new SerilogSpanProcessor(capturingLogger);

        var activity = CreateActivity(
            TelemetryConventions.AgentsAiSourceName,
            "invoke_agent",
            ("gen_ai.operation.name", "invoke_agent"),
            ("gen_ai.response.model", "claude-3"));

        processor.OnEnd(activity);

        Assert.Single(capturingLogger.Entries);
    }

    [Fact]
    public void OnEnd_MissingTags_LogsWithoutThrowing()
    {
        var capturingLogger = new CapturingLogger();
        var processor = new SerilogSpanProcessor(capturingLogger);

        var activity = CreateActivity(
            TelemetryConventions.AgentsAiSourceName,
            "invoke_agent");

        processor.OnEnd(activity);

        Assert.Single(capturingLogger.Entries);
    }
}
