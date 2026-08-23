using System.Diagnostics.Metrics;
using System.Text.Json;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamToolCatalogTests
{
    private const string PrimarySourceId = "primary";
    private const string SecondarySourceId = "secondary";
    private static readonly JsonElement DefaultSchema = JsonSerializer.SerializeToElement(new { type = "object" });

    [Fact]
    public async Task PublishSnapshot_ValidPrimarySource_Succeeds()
    {
        var catalog = new DownstreamToolCatalog();
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            PrimarySourceId,
            tools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.DegradedReason);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_DuplicateToolNamesWithinSource_RejectsSnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema),
            new("tool1", "Tool 1 duplicate", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            PrimarySourceId,
            tools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.DegradedReason);
        Assert.Contains("duplicate", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_DuplicateToolNamesBetweenSources_RejectsSecondarySnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var primaryTools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        await catalog.PublishSnapshotAsync(
            PrimarySourceId,
            primaryTools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        var secondaryTools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1 from secondary", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            secondaryTools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.DegradedReason);
        Assert.Contains("collision", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_UnexpectedTool_RejectsSnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var expectedTools = new HashSet<string>(StringComparer.Ordinal) { "tool1" };
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema),
            new("unexpected_tool", "Unexpected", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            tools,
            expectedTools,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.DegradedReason);
        Assert.Contains("unexpected", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_MissingExpectedTool_RejectsSnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var expectedTools = new HashSet<string>(StringComparer.Ordinal) { "tool1", "tool2" };
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            tools,
            expectedTools,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("missing", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_SchemaDrift_RejectsSnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var expectedSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { arg1 = new { type = "string" } },
            required = new[] { "arg1" }
        });
        var driftedSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { arg1 = new { type = "string" }, arg2 = new { type = "string" } }
        });

        var expectedTools = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["tool1"] = expectedSchema
        };
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, driftedSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            tools,
            null,
            expectedTools,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.DegradedReason);
        Assert.Contains("schema", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PublishSnapshot_ForbiddenAnnotation_RejectsSnapshot()
    {
        var catalog = new DownstreamToolCatalog();
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: false, IsDestructive: true, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            tools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.DegradedReason);
        Assert.Contains("forbidden", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetCatalogEntry_ToolInCatalog_ReturnsEntry()
    {
        var catalog = new DownstreamToolCatalog();
        var tools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        await catalog.PublishSnapshotAsync(
            PrimarySourceId,
            tools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        ToolCatalogEntry? entry = catalog.GetCatalogEntry("tool1");

        Assert.NotNull(entry);
        Assert.Equal("tool1", entry.ToolName);
        Assert.Equal(PrimarySourceId, entry.SourceId);
    }

    [Fact]
    public void GetCatalogEntry_ToolNotInCatalog_ReturnsNull()
    {
        var catalog = new DownstreamToolCatalog();

        ToolCatalogEntry? entry = catalog.GetCatalogEntry("nonexistent");

        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAllEntries_MultipleSources_ReturnsAllValidEntries()
    {
        var catalog = new DownstreamToolCatalog();
        var primaryTools = new List<DownstreamTool>
        {
            new("tool1", "Tool 1", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };
        var secondaryTools = new List<DownstreamTool>
        {
            new("tool2", "Tool 2", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        await catalog.PublishSnapshotAsync(
            PrimarySourceId,
            primaryTools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            secondaryTools,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        IReadOnlyList<ToolCatalogEntry> entries = catalog.GetAllEntries();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.ToolName == "tool1" && e.SourceId == PrimarySourceId);
        Assert.Contains(entries, e => e.ToolName == "tool2" && e.SourceId == SecondarySourceId);
    }

    [Fact]
    public async Task PublishSnapshot_RequestPolicySet_StoresPolicy()
    {
        var catalog = new DownstreamToolCatalog();
        var policy = new KubernetesMcpServerRequestPolicy(new HashSet<string> { "namespace1" });
        var tools = new List<DownstreamTool>
        {
            new(McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
                "Lists pods",
                IsReadOnly: true,
                IsDestructive: false,
                DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId,
            tools,
            null,
            null,
            policy,
            null,
            CancellationToken.None);

        Assert.True(result.IsValid);
        ToolCatalogEntry? entry = catalog.GetCatalogEntry(McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool);
        Assert.NotNull(entry);
        Assert.NotNull(entry.RequestPolicy);
    }

    [Fact]
    public void GetSourceGeneration_NeverPublished_ReturnsZero()
    {
        var catalog = new DownstreamToolCatalog();

        Assert.Equal(0, catalog.GetSourceGeneration(SecondarySourceId));
    }

    [Fact]
    public async Task PublishSnapshot_Regeneration_ReplacesPriorSnapshotAndIncrementsGeneration()
    {
        var catalog = new DownstreamToolCatalog();
        var firstGeneration = new List<DownstreamTool>
        {
            new("tool_old", "Old tool", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        await catalog.PublishSnapshotAsync(
            SecondarySourceId, firstGeneration, null, null, null, null, CancellationToken.None);
        Assert.Equal(1, catalog.GetSourceGeneration(SecondarySourceId));
        Assert.NotNull(catalog.GetCatalogEntry("tool_old"));

        // Simulates a supervised restart: the replacement process advertises a different tool set.
        var secondGeneration = new List<DownstreamTool>
        {
            new("tool_new", "New tool", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId, secondGeneration, null, null, null, null, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(2, catalog.GetSourceGeneration(SecondarySourceId));
        Assert.NotNull(catalog.GetCatalogEntry("tool_new"));
        // The tool dropped by the new generation must not remain as a stale entry.
        Assert.Null(catalog.GetCatalogEntry("tool_old"));
    }

    [Fact]
    public async Task PublishSnapshot_RegenerationCollidesWithOtherSource_RejectsAndLeavesPriorGenerationIntact()
    {
        var catalog = new DownstreamToolCatalog();
        var primaryTools = new List<DownstreamTool>
        {
            new("shared_name", "Owned by primary", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };
        await catalog.PublishSnapshotAsync(
            PrimarySourceId, primaryTools, null, null, null, null, CancellationToken.None);

        var firstGeneration = new List<DownstreamTool>
        {
            new("tool_old", "Old tool", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };
        await catalog.PublishSnapshotAsync(
            SecondarySourceId, firstGeneration, null, null, null, null, CancellationToken.None);
        Assert.Equal(1, catalog.GetSourceGeneration(SecondarySourceId));

        // The replacement process now advertises a name already owned by another source.
        var invalidSecondGeneration = new List<DownstreamTool>
        {
            new("shared_name", "Colliding replacement tool", IsReadOnly: true, IsDestructive: false, DefaultSchema)
        };

        ToolCatalogSnapshot result = await catalog.PublishSnapshotAsync(
            SecondarySourceId, invalidSecondGeneration, null, null, null, null, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("collision", result.DegradedReason, StringComparison.OrdinalIgnoreCase);
        // The prior, valid generation is untouched: same generation number, old tool still served,
        // primary's tool still owned by primary, and the rejected new tool name never appears.
        Assert.Equal(1, catalog.GetSourceGeneration(SecondarySourceId));
        Assert.NotNull(catalog.GetCatalogEntry("tool_old"));
        Assert.Equal(PrimarySourceId, catalog.GetCatalogEntry("shared_name")?.SourceId);
    }

    [Fact]
    public async Task PublishSnapshot_ConcurrentPublishesFromDifferentSources_BothSucceedWithoutPartialState()
    {
        var catalog = new DownstreamToolCatalog();
        var primaryTools = Enumerable.Range(0, 50)
            .Select(i => new DownstreamTool($"primary_tool_{i}", "Primary tool", IsReadOnly: true, IsDestructive: false, DefaultSchema))
            .ToList();
        var secondaryTools = Enumerable.Range(0, 50)
            .Select(i => new DownstreamTool($"secondary_tool_{i}", "Secondary tool", IsReadOnly: true, IsDestructive: false, DefaultSchema))
            .ToList();

        await Task.WhenAll(
            catalog.PublishSnapshotAsync(PrimarySourceId, primaryTools, null, null, null, null, CancellationToken.None),
            catalog.PublishSnapshotAsync(SecondarySourceId, secondaryTools, null, null, null, null, CancellationToken.None));

        IReadOnlyList<ToolCatalogEntry> entries = catalog.GetAllEntries();
        Assert.Equal(100, entries.Count);
        Assert.All(primaryTools, t => Assert.Equal(PrimarySourceId, catalog.GetCatalogEntry(t.Name)?.SourceId));
        Assert.All(secondaryTools, t => Assert.Equal(SecondarySourceId, catalog.GetCatalogEntry(t.Name)?.SourceId));
    }

    [Fact]
    public void RecordSourceHealthy_AfterDegraded_RecordsDegradedDurationMetric()
    {
        var timeProvider = new ManualTimeProvider();
        var catalog = new DownstreamToolCatalog(timeProvider);

        var recorded = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamDegradedDurationHistogramName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            recorded.Add(new Measurement<double>(value, tags)));
        listener.Start();

        catalog.RecordSourceDegraded(SecondarySourceId, "source unavailable");
        timeProvider.Advance(TimeSpan.FromMilliseconds(2500));
        catalog.RecordSourceHealthy(SecondarySourceId);

        Assert.Single(recorded);
        Assert.Equal(2500d, recorded[0].Value, precision: 0);
        KeyValuePair<string, object?>[] tags = recorded[0].Tags.ToArray();
        Assert.Equal(
            SecondarySourceId,
            (string?)tags.First(t => t.Key == McpGatewayConventions.Telemetry.Tags.Source).Value);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
}
