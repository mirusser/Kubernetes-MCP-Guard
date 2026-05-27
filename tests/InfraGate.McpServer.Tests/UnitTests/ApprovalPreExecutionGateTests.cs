using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class ApprovalPreExecutionGateTests
{
    [Fact]
    public async Task EvaluateAsync_GrantedPlan_PublishesGrantValidatedAudit()
    {
        var store = CreateStore();
        var envelope = CreatePlanEnvelope();
        var created = await store.CreatePlanAsync(envelope, "demo", CancellationToken.None);
        var grant = await store.CreateGrantAsync(created.Envelope, "requester", "challenge-1", CancellationToken.None);
        var outbox = new RecordingApprovalAuditOutbox();
        var gate = new ApprovalPreExecutionGate(store, outbox);

        var result = await gate.EvaluateAsync(
            envelope.Id,
            new PassingDomainPlanExecutor(),
            CancellationToken.None);

        Assert.True(result.IsPassed);
        var audit = Assert.Single(outbox.Events);
        Assert.Equal(ApprovalConventions.AuditEvents.PreExecutionGrantValidated, audit.EventName);
        var payload = Assert.IsType<PreExecutionGrantValidatedPayload>(audit.Payload);
        Assert.Equal(created.Envelope.Id, payload.PlanId);
        Assert.Equal(grant.Id, payload.GrantId);
        Assert.Equal(created.Envelope.IntentDigest, payload.IntentDigest);
        Assert.Equal(created.Envelope.ReviewDigest, payload.ReviewDigest);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPendingPlan_ReturnsReasonCode()
    {
        var store = CreateStore();
        var gate = new ApprovalPreExecutionGate(store, new RecordingApprovalAuditOutbox());

        var result = await gate.EvaluateAsync(
            ApprovalIds.NewPlanId(),
            new PassingDomainPlanExecutor(),
            CancellationToken.None);

        Assert.False(result.IsPassed);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotPending, result.ReasonCode);
    }

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-pre-execution-tests", Guid.NewGuid().ToString("N"));
        return new ApprovalStore(new ApprovalStoreOptions(root));
    }

    private static PlanEnvelope<Dictionary<string, string>> CreatePlanEnvelope() =>
        PlanEnvelopeFactory.Create(
            ApprovalIds.NewPlanId(),
            "dummy",
            "scale",
            DateTimeOffset.UtcNow,
            new PlanRequester("requester", "test"),
            ApprovalDigest.ComputeSha256(
                "dummy.intent.v1",
                new
                {
                    operation = "scale",
                    name = "demo",
                    replicas = "2"
                }),
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            new Dictionary<string, string>
            {
                ["name"] = "demo",
                ["replicas"] = "2"
            });

    private sealed class PassingDomainPlanExecutor : IDomainPlanExecutor
    {
        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Success("Pre-execution checks passed.", "demo"));

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Success("Executed.", "demo"));
    }

    private sealed class RecordingApprovalAuditOutbox : IApprovalAuditOutbox
    {
        public List<ApprovalAuditEntry> Events { get; } = [];

        public Task<long> AppendAsync(ApprovalAuditEntry entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            return Task.FromResult(0L);
        }
    }
}
