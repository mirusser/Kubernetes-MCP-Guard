using System.Globalization;

namespace InfraGate.Observer.E2E.Tests.Workflows;

[Trait("Category", "ObserverE2E")]
[Collection(ObserverE2ECollection.Name)]
public sealed class ObserverE2EWorkflowTests(ObserverE2EFixture fixture)
{

    [Fact]
    public void ObserverE2E_DisabledByDefault_DoesNotRequireExternalDependencies()
    {
        if (Environment.GetEnvironmentVariable(ObserverE2EFixture.EnableEnvVar) == "1")
        {
            return;
        }

        Assert.False(fixture.IsEnabled);
    }

    [Fact]
    public async Task ObserverIsHealthy_WhenEnabled()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        bool healthy = await fixture.HealthAsync(CancellationToken.None);
        Assert.True(healthy);
    }

    [Fact]
    public async Task ObserveNow_WhenEnabled_ReturnsStructuralAnomalyReports()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var reports = await fixture.ObserveNowAsync(CancellationToken.None);

        Assert.All(reports, AssertReportShape);
    }

    [Fact]
    public async Task ObserveNow_RealLlmPath_UsesSameStructuralContract()
    {
        if (!fixture.IsEnabled || !fixture.IsRealLlmEnabled)
        {
            return;
        }

        var reports = await fixture.ObserveNowAsync(CancellationToken.None);

        Assert.All(reports, AssertReportShape);
    }

    [Fact]
    public async Task Reports_AnomalyIdIsHash_CycleIdIsGuid()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var reports = await fixture.ObserveNowAsync(CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.All(reports, report =>
        {
            Assert.True(report.AnomalyId.Length >= 8, "AnomalyId should be a non-trivial hash string");
            Assert.False(Guid.TryParse(report.AnomalyId, CultureInfo.InvariantCulture, out _), "AnomalyId should not be a GUID");
            Assert.True(Guid.TryParse(report.CycleId, CultureInfo.InvariantCulture, out _), "CycleId should be a GUID");
        });
    }

    [Fact]
    public async Task Reports_AnomalyIdStableAcrossCycles()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var first = await fixture.ObserveNowAsync(CancellationToken.None);
        var second = await fixture.ObserveNowAsync(CancellationToken.None);

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(report => report.AnomalyId).Order(StringComparer.Ordinal),
            second.Select(report => report.AnomalyId).Order(StringComparer.Ordinal));
    }

    private static void AssertReportShape(AnomalyReport report)
    {
        Assert.False(string.IsNullOrWhiteSpace(report.AnomalyId));
        Assert.False(string.IsNullOrWhiteSpace(report.CycleId));
        Assert.True(Enum.IsDefined(report.Kind));
        Assert.True(Enum.IsDefined(report.Severity));
        Assert.True(Enum.IsDefined(report.Status));
        Assert.False(string.IsNullOrWhiteSpace(report.Target.ApiVersion));
        Assert.False(string.IsNullOrWhiteSpace(report.Target.Kind));
        Assert.False(string.IsNullOrWhiteSpace(report.Target.Namespace));
        Assert.False(string.IsNullOrWhiteSpace(report.Target.Name));
    }
}
