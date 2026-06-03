namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class CompositeModelVisibleContentGuardTests
{
    private static readonly ModelVisibleContent SampleContent = new(
        "original hostile text",
        ModelVisibleContentSource.PlannerAnomaly,
        "planner-agent",
        CorrelationId: "corr-42");

    private static ModelVisibleContentOptions DefaultOptions => new()
    {
        MaximumInputCharacters = 100_000,
    };

    private static AgentGuardrailMetrics CreateMetrics() =>
        new(new Meter($"test-composite-{Guid.NewGuid()}"));

    private static ILogger<CompositeModelVisibleContentGuard> NullLog => new NullTestLogger();

    private sealed class NullTestLogger : ILogger<CompositeModelVisibleContentGuard>
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }

        public bool IsEnabled(LogLevel logLevel) => false;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    [Fact]
    public async Task EvaluateAsync_NoGuards_ReturnsAllow()
    {
        var sut = new CompositeModelVisibleContentGuard([], CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Allow, decision.Action);
        Assert.Equal(SampleContent.Text, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_SingleAllowGuard_ReturnsOriginalText()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Allow, SampleContent.Text)],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Allow, decision.Action);
        Assert.Equal(SampleContent.Text, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_SingleRedactGuard_ReturnsRedactedText()
    {
        const string redactedText = "[REDACTED]";
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Redact, redactedText)],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Redact, decision.Action);
        Assert.Equal(redactedText, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_StrongerActionWins_WhenWeakFollowedByStrong()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [
                new FixedGuard(ModelVisibleContentAction.Allow, SampleContent.Text),
                new FixedGuard(ModelVisibleContentAction.Quarantine, "should not appear"),
            ],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Quarantine, decision.Action);
    }

    [Fact]
    public async Task EvaluateAsync_StrongerActionWins_WhenStrongFollowedByWeak()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [
                new FixedGuard(ModelVisibleContentAction.BlockModelIngestion, "should not appear"),
                new FixedGuard(ModelVisibleContentAction.Redact, "redacted"),
            ],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.BlockModelIngestion, decision.Action);
    }

    [Fact]
    public async Task EvaluateAsync_Quarantine_ReturnsQuarantinePlaceholder()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Quarantine, "hostile text must not appear")],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(AgentGuardrailConventions.DefaultQuarantinePlaceholder, decision.Text);
        Assert.DoesNotContain(SampleContent.Text, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_BlockModelIngestion_ReturnsBlockedPlaceholder()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.BlockModelIngestion, "hostile text must not appear")],
            CreateMetrics(), NullLog, DefaultOptions);

        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(AgentGuardrailConventions.DefaultBlockedPlaceholder, decision.Text);
        Assert.DoesNotContain(SampleContent.Text, decision.Text);
    }

    [Fact]
    public async Task EvaluateAsync_BlockSeen_StopsEvaluatingSubsequentGuards()
    {
        var trackingGuard = new TrackingGuard(ModelVisibleContentAction.Allow, SampleContent.Text);
        var sut = new CompositeModelVisibleContentGuard(
            [
                new FixedGuard(ModelVisibleContentAction.BlockModelIngestion, "blocked"),
                trackingGuard,
            ],
            CreateMetrics(), NullLog, DefaultOptions);

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(0, trackingGuard.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_RedactThenAllow_AccumulatesRedaction()
    {
        const string redacted = "REDACTED_BY_STAGE1";
        var stage2 = new CapturingGuard(ModelVisibleContentAction.Allow);
        var sut = new CompositeModelVisibleContentGuard(
            [
                new FixedGuard(ModelVisibleContentAction.Redact, redacted),
                stage2,
            ],
            CreateMetrics(), NullLog, DefaultOptions);

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        // Stage 2 should have received the redacted text, not the original.
        Assert.Equal(redacted, stage2.LastReceivedText);
    }

    [Fact]
    public async Task EvaluateAsync_Quarantine_CallsAuditWithOriginalContent()
    {
        var audit = new RecordingAudit();
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Quarantine, "q-text")],
            CreateMetrics(), NullLog, DefaultOptions, audit);

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Single(audit.Recorded);
        Assert.NotNull(audit.Recorded[0].Digest);
        Assert.NotEmpty(audit.Recorded[0].Digest);
        Assert.Equal(SampleContent.Source, audit.Recorded[0].Source);
    }

    [Fact]
    public async Task EvaluateAsync_Block_CallsAuditWithOriginalContent()
    {
        var audit = new RecordingAudit();
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.BlockModelIngestion, "b-text")],
            CreateMetrics(), NullLog, DefaultOptions, audit);

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Single(audit.Recorded);
        Assert.NotNull(audit.Recorded[0].Digest);
        Assert.NotEmpty(audit.Recorded[0].Digest);
    }

    [Fact]
    public async Task EvaluateAsync_Allow_DoesNotCallAudit()
    {
        var audit = new RecordingAudit();
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Allow, SampleContent.Text)],
            CreateMetrics(), NullLog, DefaultOptions, audit);

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Empty(audit.Recorded);
    }

    [Fact]
    public async Task EvaluateAsync_AuditThrows_StillReturnsGuardDecision()
    {
        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Quarantine, "q-text")],
            CreateMetrics(), NullLog, DefaultOptions, new FailingAudit());

        // Must not throw — audit failure is isolated.
        var decision = await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Equal(ModelVisibleContentAction.Quarantine, decision.Action);
    }

    [Fact]
    public async Task EvaluateAsync_AuditThrows_RecordsDegradedMetric()
    {
        using var testMeter = new Meter($"test-composite-degraded-{Guid.NewGuid()}");
        var metrics = new AgentGuardrailMetrics(testMeter);

        var degraded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.ModelVisibleDegradedCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            degraded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var sut = new CompositeModelVisibleContentGuard(
            [new FixedGuard(ModelVisibleContentAction.Quarantine, "q")],
            metrics, NullLog, DefaultOptions, new FailingAudit());

        await sut.EvaluateAsync(SampleContent, CancellationToken.None);

        Assert.Single(degraded);
        Assert.Equal(1L, degraded[0].Value);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FixedGuard(ModelVisibleContentAction action, string text) : IModelVisibleContentGuard
    {
        public Task<ModelVisibleContentDecision> EvaluateAsync(
            ModelVisibleContent content,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ModelVisibleContentDecision(action, text, [], AgentGuardrailConventions.Reasons.None));
    }

    private sealed class TrackingGuard(ModelVisibleContentAction action, string text) : IModelVisibleContentGuard
    {
        public int CallCount { get; private set; }

        public Task<ModelVisibleContentDecision> EvaluateAsync(
            ModelVisibleContent content,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ModelVisibleContentDecision(action, text, [], AgentGuardrailConventions.Reasons.None));
        }
    }

    private sealed class CapturingGuard(ModelVisibleContentAction action) : IModelVisibleContentGuard
    {
        public string? LastReceivedText { get; private set; }

        public Task<ModelVisibleContentDecision> EvaluateAsync(
            ModelVisibleContent content,
            CancellationToken cancellationToken)
        {
            LastReceivedText = content.Text;
            return Task.FromResult(new ModelVisibleContentDecision(action, content.Text, [], AgentGuardrailConventions.Reasons.None));
        }
    }

    private sealed class RecordingAudit : IModelVisibleContentAudit
    {
        public List<(string Digest, ModelVisibleContentSource Source, string AgentName, ModelVisibleContentDecision Decision)> Recorded = [];

        public Task PersistAsync(
            string digest, ModelVisibleContentSource source, string agentName,
            ModelVisibleContentDecision decision,
            CancellationToken cancellationToken)
        {
            Recorded.Add((digest, source, agentName, decision));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAudit : IModelVisibleContentAudit
    {
        public Task PersistAsync(
            string digest, ModelVisibleContentSource source, string agentName,
            ModelVisibleContentDecision decision,
            CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Simulated audit store failure"));
    }
}
