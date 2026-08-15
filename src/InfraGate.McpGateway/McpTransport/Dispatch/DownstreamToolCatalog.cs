using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace InfraGate.McpGateway;

/// <summary>
/// Maintains the Gateway's published tool catalog with immutable source ownership.
/// Each tool maps to exactly one source and its reviewed schema.
/// </summary>
internal sealed class DownstreamToolCatalog(TimeProvider? timeProvider = null)
{
    private static readonly Meter Meter = new(
        McpGatewayConventions.Telemetry.MeterName,
        McpGatewayConventions.Telemetry.MeterVersion);

    private static readonly Histogram<double> DegradedDurationHistogram =
        Meter.CreateHistogram<double>(McpGatewayConventions.Telemetry.DownstreamDegradedDurationHistogramName, unit: "ms");

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock publishLock = new();
    private readonly ConcurrentDictionary<string, string> degradedSources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> sourceGenerations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> degradedSinceTimestamps = new(StringComparer.Ordinal);

    // Swapped as a single reference under publishLock. Readers (GetCatalogEntry/GetAllEntries)
    // capture this field once and see either a fully-old or fully-new snapshot, never a partial
    // one, even while a source is mid-regeneration (e.g. after a supervised process restart).
    private volatile ImmutableDictionary<string, ToolCatalogEntry> catalog =
        ImmutableDictionary<string, ToolCatalogEntry>.Empty;

    /// <summary>
    /// Validates and publishes a source's tool snapshot, replacing any prior snapshot the same
    /// source previously published (a "regeneration", e.g. after a supervised process restart).
    /// Duplicate names, schema drift, forbidden annotations, or unexpected tools reject the
    /// entire snapshot and record a degraded reason; the catalog is left exactly as it was, so a
    /// failed regeneration never serves a partially-initialized or unvalidated tool set.
    /// </summary>
    public Task<ToolCatalogSnapshot> PublishSnapshotAsync(
        string sourceId,
        IReadOnlyList<DownstreamTool> tools,
        IReadOnlySet<string>? expectedTools = null,
        IReadOnlyDictionary<string, JsonElement>? expectedToolSchemas = null,
        KubernetesMcpServerRequestPolicy? requestPolicy = null,
        KubernetesMcpServerResponsePolicy? responsePolicy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(tools);

        if (!ValidateSnapshot(
            tools,
            expectedTools,
            expectedToolSchemas,
            out string? degradedReason))
        {
            return Task.FromResult(new ToolCatalogSnapshot(
                IsValid: false,
                degradedReason,
                Entries: []));
        }

        lock (publishLock)
        {
            // Start from the current snapshot with this source's own prior entries removed, so a
            // regenerated snapshot that drops a previously-advertised tool doesn't leave a stale
            // entry behind, and so this source's own old entries never collide with its new ones.
            ImmutableDictionary<string, ToolCatalogEntry> candidate = catalog;
            foreach (KeyValuePair<string, ToolCatalogEntry> existingEntry in catalog)
            {
                if (string.Equals(existingEntry.Value.SourceId, sourceId, StringComparison.Ordinal))
                {
                    candidate = candidate.Remove(existingEntry.Key);
                }
            }

            var entries = new List<ToolCatalogEntry>();
            foreach (DownstreamTool tool in tools)
            {
                // Check for collision with existing catalog entries from other sources
                if (candidate.TryGetValue(tool.Name, out ToolCatalogEntry? existing) &&
                    !string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal))
                {
                    return Task.FromResult(new ToolCatalogSnapshot(
                        IsValid: false,
                        DegradedReason: $"Tool name collision: '{tool.Name}' already registered by source '{existing.SourceId}'.",
                        Entries: []));
                }

                // Check for forbidden annotations (secondary sources must be read-only)
                if (!IsSecondarySourceAllowed(sourceId, tool, out string? forbiddenReason))
                {
                    return Task.FromResult(new ToolCatalogSnapshot(
                        IsValid: false,
                        DegradedReason: forbiddenReason,
                        Entries: []));
                }

                // Every advertised tool is published, even ones a request policy will later deny.
                // The catalog is the single routing authority (calls must go through it, not a
                // second name lookup), so denial has to happen at call time via
                // RequestPolicy.TryValidate (with an audit trail) rather than by silently omitting
                // the entry. Callers that only want the approved-and-visible subset should consult
                // RequestPolicy.IsToolAllowed themselves (see GatewayToolDispatcher.ListToolsAsync).
                var entry = new ToolCatalogEntry(
                    tool.Name,
                    sourceId,
                    tool,
                    requestPolicy,
                    responsePolicy);

                entries.Add(entry);
                candidate = candidate.SetItem(tool.Name, entry);
            }

            // Single reference assignment: the only point at which readers can observe this
            // source's new generation, and only after every tool in it has passed validation.
            catalog = candidate;
            sourceGenerations.AddOrUpdate(sourceId, 1, static (_, generation) => generation + 1);

            return Task.FromResult(new ToolCatalogSnapshot(
                IsValid: true,
                DegradedReason: null,
                Entries: entries));
        }
    }

    /// <summary>
    /// Gets the number of times the given source has successfully published a validated
    /// snapshot (its "generation"). Zero means the source has never published successfully.
    /// </summary>
    public long GetSourceGeneration(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return sourceGenerations.GetValueOrDefault(sourceId);
    }

    /// <summary>
    /// Gets a catalog entry by tool name.
    /// </summary>
    public ToolCatalogEntry? GetCatalogEntry(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return catalog.TryGetValue(toolName, out ToolCatalogEntry? entry) ? entry : null;
    }

    /// <summary>
    /// Gets all catalog entries.
    /// </summary>
    public IReadOnlyList<ToolCatalogEntry> GetAllEntries()
    {
        return catalog.Values.ToList();
    }

    /// <summary>
    /// Records that a source's snapshot could not be published this cycle (e.g. an optional
    /// secondary source that timed out, was unreachable, or failed validation). The reason must
    /// already be a stable, sanitized string — never raw exception text or a response body —
    /// since it may later be surfaced through health/readiness reporting.
    /// </summary>
    public void RecordSourceDegraded(string sourceId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        degradedSources[sourceId] = reason;
        degradedSinceTimestamps.TryAdd(sourceId, clock.GetTimestamp());
    }

    /// <summary>
    /// Clears a source's degraded state after it successfully publishes a valid snapshot.
    /// </summary>
    public void RecordSourceHealthy(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        degradedSources.TryRemove(sourceId, out _);
        if (degradedSinceTimestamps.TryRemove(sourceId, out long since))
        {
            DegradedDurationHistogram.Record(
                clock.GetElapsedTime(since).TotalMilliseconds,
                new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.Source, sourceId));
        }
    }

    /// <summary>
    /// Gets the sanitized degraded reason for every source currently omitted from the catalog.
    /// An empty result means every configured source published successfully.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetDegradedSources()
    {
        return degradedSources.ToDictionary(StringComparer.Ordinal);
    }

    private static bool ValidateSnapshot(
        IReadOnlyList<DownstreamTool> tools,
        IReadOnlySet<string>? expectedTools,
        IReadOnlyDictionary<string, JsonElement>? expectedToolSchemas,
        out string? degradedReason)
    {
        // Check for duplicate tool names within the snapshot
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (DownstreamTool tool in tools)
        {
            if (!seenNames.Add(tool.Name))
            {
                degradedReason = $"Duplicate tool name '{tool.Name}' in snapshot.";
                return false;
            }
        }

        // Check for unexpected tools
        if (expectedTools is not null)
        {
            foreach (DownstreamTool tool in tools)
            {
                if (!expectedTools.Contains(tool.Name))
                {
                    degradedReason = $"Unexpected tool '{tool.Name}' not in approved set.";
                    return false;
                }
            }
        }

        // Check for schema drift
        if (expectedToolSchemas is not null)
        {
            foreach (DownstreamTool tool in tools)
            {
                if (expectedToolSchemas.TryGetValue(tool.Name, out JsonElement expectedSchema))
                {
                    if (!JsonElementsEqual(tool.InputSchema, expectedSchema))
                    {
                        degradedReason = $"Schema drift detected for tool '{tool.Name}'.";
                        return false;
                    }
                }
            }
        }

        degradedReason = null;
        return true;
    }

    private static bool IsSecondarySourceAllowed(
        string sourceId,
        DownstreamTool tool,
        out string? forbiddenReason)
    {
        // Secondary sources (non-primary) must only expose read-only tools
        if (!string.Equals(sourceId, McpGatewayConventions.DownstreamSources.Primary, StringComparison.Ordinal))
        {
            if (!tool.IsReadOnly)
            {
                forbiddenReason = $"Forbidden annotation: secondary source '{sourceId}' tool '{tool.Name}' is not read-only.";
                return false;
            }
        }

        forbiddenReason = null;
        return true;
    }

    private static bool JsonElementsEqual(JsonElement a, JsonElement b)
    {
        string aJson = JsonSerializer.Serialize(a);
        string bJson = JsonSerializer.Serialize(b);
        return string.Equals(aJson, bJson, StringComparison.Ordinal);
    }
}
