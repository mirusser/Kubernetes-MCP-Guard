namespace InfraGate.Safety.E2E.Tests;

// Layout deviation from .agents/skills/writing-tests/SKILL.md, which prescribes
// `UnitTests/` (no network/K8s) and `IntegrationTests/` (real cluster, opt-in).
//
// This project as a whole IS the integration tier: every test is opt-in behind
// INFRA_GATE_RUN_SAFETY_E2E=1, requires Docker (Keycloak via Testcontainers) and
// a real Kubernetes cluster, and is gated by [Trait("Category", "SafetyE2E")]
// so the default test pass excludes it. Putting an IntegrationTests/ folder
// inside an already-integration-only project would be redundant.
//
// Inside that scope, tests are organised by demo workflow rather than by
// production class because the seven properties from
// .agents/Plans/minimum-for-demo.md §6 are vertical end-to-end stories spanning
// the gateway, McpServer subprocess, ApprovalStore, and Kubernetes API — there
// is no single production class per file. One Workflows/<Property>Tests.cs
// keeps each safety property a single readable file usable as a demo artefact.
[CollectionDefinition(Name)]
public sealed class SafetyE2ECollection : ICollectionFixture<SafetyE2EFixture>
{
    public const string Name = "SafetyE2E";
}
