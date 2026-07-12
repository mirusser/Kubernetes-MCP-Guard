using InfraGate.ApprovalUi;
using InfraGate.AuditOutbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraGate.ApprovalUi.Tests.UnitTests;

public sealed class AuditTimelinePageTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenderAuditTimelinePageAsync_WithEntries_ContainsTimelineSections()
    {
        await using var renderer = CreateRenderer();
        var data = new AuditTimelinePageData(
            "plan-1",
            "anomaly-1",
            [
                new AuditTimelineEntry(
                    FixedTime,
                    AuditOutboxConventions.Streams.Observer,
                    "anomaly.detected",
                    "service:observer",
                    null,
                    "detected",
                    null,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["namespace"] = "mcp-ns",
                        ["operation"] = "scale"
                    })
            ]);

        var html = await renderer.RenderAuditTimelinePageAsync(data);

        Assert.Contains("data-section=\"timeline-entries\"", html);
        Assert.Contains("data-field=\"plan-id\"", html);
        Assert.Contains("plan-1", html);
        Assert.Contains("data-stream=\"observer\"", html);
        Assert.Contains("data-event=\"anomaly.detected\"", html);
        Assert.Contains("data-field=\"event-name\"", html);
        Assert.Contains("data-field=\"actor\"", html);
        Assert.Contains("service:observer", html);
        Assert.Contains("data-field=\"outcome\"", html);
        Assert.Contains("detected", html);
        Assert.Contains("data-field-label=\"namespace\"", html);
        Assert.Contains("data-field-value=\"namespace\"", html);
        Assert.Contains("mcp-ns", html);
    }

    [Fact]
    public async Task RenderAuditTimelinePageAsync_EmptyEntries_ShowsEmptyState()
    {
        await using var renderer = CreateRenderer();
        var data = new AuditTimelinePageData("plan-missing", null, []);

        var html = await renderer.RenderAuditTimelinePageAsync(data);

        Assert.Contains("data-section=\"timeline-empty\"", html);
        Assert.Contains("No audit events were found", html);
        Assert.DoesNotContain("data-section=\"timeline-entries\"", html);
    }

    [Fact]
    public async Task RenderAuditTimelinePageAsync_EntryWithoutDisplayFields_DoesNotRenderFieldsGrid()
    {
        await using var renderer = CreateRenderer();
        var data = new AuditTimelinePageData(
            "plan-1",
            null,
            [
                new AuditTimelineEntry(
                    FixedTime,
                    AuditOutboxConventions.Streams.Approvals,
                    "challenge.created",
                    "user@example.com",
                    null,
                    null,
                    null,
                    new Dictionary<string, string?>(StringComparer.Ordinal))
            ]);

        var html = await renderer.RenderAuditTimelinePageAsync(data);

        Assert.Contains("data-section=\"timeline-entries\"", html);
        Assert.DoesNotContain("class=\"fields\"", html);
    }

    private static ApprovalPageRenderer CreateRenderer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        return new ApprovalPageRenderer(provider, loggerFactory);
    }
}
