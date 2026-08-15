# Kubernetes MCP Server Integration Hardening Plan

**Date:** 2026-08-09
**Status:** Implementation complete (Tasks 1–21, Checkpoints A–H); all human-review checkpoint sign-offs still await the user. The Task 18 hosted-CI proof is no longer blocked on an offline self-hosted runner — `.github/workflows/integration-tests.yml` has been migrated to `ubuntu-latest` with its own disposable minikube cluster and demo workload per `.agents/Plans/2026-08-14-migrate-integration-tests-ci-to-hosted-runner.md`, but a live validated hosted run (and re-enabling `pull_request`/`push` triggers) is still pending. Nothing in this plan has been committed or pushed.
**Target branch:** `feature/mcp-server`

## Goal

Turn the current basic read-only integration with `containers/kubernetes-mcp-server` into a deliberately constrained, observable, recoverable adapter without weakening InfraGate's existing mutation approval boundary. The upstream server remains a secondary read source; the in-repository `InfraGate.McpServer` remains the only mutation path until a separately reviewed evidence-parity gate is satisfied.

## Context

The Gateway currently starts `kubernetes-mcp-server` as an optional stdio downstream, merges selected tools into its catalog, and can perform basic Kubernetes reads. The architecture review found that the integration works as a proof of concept but still trusts too many upstream defaults and loses important MCP semantics between the downstream and the Gateway.

Material findings behind this plan:

- The repository pins upstream `v0.0.64` in both `scripts/install-kubernetes-mcp-server.sh` and `deploy/docker/mcp-gateway.Dockerfile`. The latest reviewed release is `v0.0.66` (2026-07-31).
- A binary produced by the current `go install` path reports `0.0.0`. The official `v0.0.66` linux-amd64 release binary is static, reports `v0.0.66`, and has SHA-256 `692a7b283a96140311fd46f13b8373657b2e9bfe660a36bb6434e8c42d899dbc`.
- The secondary receives the primary mutation-capable `infra-gate-mcp` kubeconfig. An unused `infra-gate-mcp-view` ServiceAccount and `infra-gate-mcp-viewer` Role already exist in `deploy/minikube/rbac.yaml`.
- The viewer Role needs `get` on `pods/log` if log access remains enabled. It must not gain Secret access.
- The current tool allowlist includes cluster-wide `pods_list`; the namespace-scoped `pods_list_in_namespace` is the appropriate replacement.
- Upstream defaults allow multi-cluster discovery and optional namespaces. `resources_get` returns raw objects, including ConfigMap data, and would expose Secrets if RBAC ever allowed them. `pods_log` can be effectively unbounded when `tail` is absent.
- The Gateway should enforce a source-specific allowlist and arguments policy itself. `ReadOnlyHint` is useful metadata, not an authorization decision.
- `DownstreamMcpClient` reduces a downstream result to text and drops `isError`, `structuredContent`, `_meta`, and non-text content. Tool or transport failures can consequently be returned in a model-visible envelope marked `status: success`.
- `DownstreamToolRegistry` initializes once for the process lifetime. It does not validate name/schema collisions, and failure of the optional secondary can fail the merged `tools/list` operation.
- The child process lacks supervised restart/backoff, secondary readiness, and source-tagged telemetry. Gateway readiness currently covers PostgreSQL only. W3C trace context is not injected into downstream MCP `_meta`.
- Observer and Planner discover upstream tools through `AgentMcpToolset`, but their Tool-Call Guardrails use fixed original InfraGate tool names, so the newly discovered upstream calls are blocked.
- The existing real-binary integration test can silently skip, manually builds the secondary rather than production composition, passes an unsupported namespace argument to `pods_list`, and does not assert that the upstream result has `isError == false`. It can therefore pass without proving a successful read.
- The integration workflow is manual/self-hosted only.
- ADR-0021 anticipates eventual removal of `InfraGate.McpServer`; ADR-0033 intentionally keeps upstream read-only until mutation evidence parity exists. Upstream PR #1026 is currently open and merge-dirty, and covers only create/update dry-run—not delete, scale, restart, set-image, diff, or freshness evidence.

## Restated Request

Create an implementation-ready, dependency-ordered plan covering the architectural findings and recommendations for tightening the current Kubernetes MCP integration. The plan must preserve defense in depth, use real binaries and Kubernetes for integration evidence, and defer any upstream mutation routing until explicit evidence parity and human approval.

## Assumptions

- Production integration is single-cluster and single-context. Multi-cluster discovery is out of scope for this hardening increment.
- Every enabled environment supplies a non-empty namespace allowlist; wildcard namespace access fails configuration validation.
- The initial safe tool set is `pods_list_in_namespace`, `pods_get`, and `pods_log`. `events_list` is excluded because `v0.0.66` has no server-side result limit and materializes the full list before the Gateway can apply its response cap.
- `resources_get` is disabled initially. If later required, a separate reviewed projection policy must deny Secrets, strip ConfigMap `data` and `binaryData`, constrain GVKs, and remain under response-size limits.
- `pods_log` requires an explicit `tail` value and caps it at 200 lines. Every secondary result is capped at 256 KiB after sanitization; these starting values remain configurable only within centrally validated upper bounds.
- The secondary is optional for Gateway availability: primary tools stay available if it fails, while health details and telemetry report the secondary as degraded.
- No mocking packages will be added. Unit tests may use existing hand-written fakes; integration claims require the real upstream binary and a real disposable Kubernetes cluster.
- Implementation continues on the existing `feature/mcp-server` worktree. This review does not create commits or change commit boundaries.

## Non-goals

- Routing create, update, delete, scale, restart, set-image, or any other mutation to the upstream server.
- Removing `InfraGate.McpServer` in this increment.
- Supporting arbitrary raw Kubernetes resource reads, Secrets, unrestricted logs, cluster-wide listing, or multi-cluster operation.
- Depending on upstream PR #1026 before it is merged, released, and independently proven against InfraGate's evidence contract.
- Implementing dynamic MCP `tools/list_changed` handling. A fixed catalog per supervised child-process generation is intentional for now.

## Success Criteria

- The upstream child process authenticates only as the viewer identity and cannot mutate resources or read Secrets according to live `kubectl auth can-i` checks.
- The Gateway exposes only the exact approved secondary tools, validates the dedicated kubeconfig context, namespace and arguments, rejects raw resource kinds, and bounds log and final model-visible result size.
- MCP success, error, content, structured content, and metadata semantics survive the downstream-to-Gateway path; failures cannot be labelled successful.
- Primary `tools/list` and calls remain usable when the optional secondary is missing, unhealthy, colliding, or restarting.
- Secondary lifecycle, readiness/degraded state, calls, errors, latency, restarts, catalog generation, and trace propagation are observable by source.
- Local and CI integration tests prove a successful namespaced read using the actual pinned release binary and production composition, and fail—not silently skip—when required CI infrastructure is absent.
- Observer and Planner receive a single reviewed diagnostic-read capability projection that agrees with their Tool-Call Guardrails.
- Mutation routing remains unchanged, and the future replacement decision is represented by a concrete evidence matrix with a default `no-go` result until every required capability passes.

## Architecture Decisions

1. **Treat the upstream server as an untrusted read adapter.** Kubernetes RBAC is the outer boundary; Gateway source policy, argument validation, sanitization, and result caps are independent inner boundaries.
2. **Fail closed on raw or unbounded reads.** Disable `resources_get` rather than attempting a broad generic redactor in the first increment, and disable `events_list` until upstream supports a reviewed server-side result limit. Secret access is denied both by RBAC and Gateway policy.
3. **Use one authoritative federated catalog.** Every tool has an immutable source identity and expected schema. A bad optional secondary catalog is rejected as a unit without removing primary tools.
4. **Preserve MCP results as typed data.** Transport, policy, sanitization, dispatch, and HTTP MCP response layers operate on a typed result rather than a text-only string.
5. **Keep catalog lifecycle simple.** Configure upstream single-context, core-only, fixed-tool, and stateless. Refresh its catalog only after a supervised child-process restart, then atomically replace that source's snapshot.
6. **Acquire a verified adapter bundle.** One version/checksum manifest and one installer path serve local and container builds. Unsupported OS/architecture tuples fail explicitly.
7. **Project one diagnostic-read capability profile.** Tool discovery and agent guardrails consume the same exact-name profile; neither infers authorization from `ReadOnlyHint`.
8. **Make replacement evidence-gated.** The future upstream mutation track cannot alter production routing until capability, evidence, approval, audit, freshness, and rollback checks all pass and an ADR records an explicit go decision.

## Dependency Graph

```text
Trust decision and safe defaults
    |
    +--> Viewer identity + fixed upstream configuration
    |        |
    |        +--> Gateway read policy + safe projections
    |                 |
    |                 +--> Typed MCP results + structured sanitization
    |                          |
    |                          +--> Federated catalog isolation
    |                                   |
    |                                   +--> Process supervision + health + telemetry
    |                                            |
    |                                            +--> Verified artifact + production composition
    |                                                     |
    |                                                     +--> Real-binary CI evidence
    |                                                              |
    |                                                              +--> Agent diagnostic profile
    |
    +--> Mutation evidence contract --------------------------------+--> Future swap decision
```

## Phased Task Checklist

### Phase 1: Freeze the Trust Boundary

#### Task 1: Amend the architecture decision for the hardened secondary

**Description:** Update the relevant ADRs and Gateway documentation to state that the upstream process is a constrained, optional read adapter; document the layered trust boundary, the fail-closed raw-read decision, and the explicit evidence gate for any future mutation use.

**Acceptance criteria:**

- [x] ADR-0033 records the viewer identity, exact Gateway policy, optional/degraded behavior, and immutable-per-process catalog decision.
- [x] ADR-0021 and ADR-0033 agree that `InfraGate.McpServer` removal is future work gated by evidence parity rather than the current upstream roadmap.
- [x] `resources_get`, multi-cluster operation, and upstream mutation routing are explicitly out of scope for this increment.

**Verification:** Review the rendered ADR links and run `rtk grep "evidence parity" docs/adr src/InfraGate.McpGateway/README.md`.

**Dependencies:** None.

**Files likely touched:** `docs/adr/0033-kubernetes-mcp-server-readonly-secondary-downstream.md`, `docs/adr/0021-*.md`, `src/InfraGate.McpGateway/README.md`.

**Estimated scope:** Small (2–3 files).

#### Task 2: Give the secondary a dedicated viewer kubeconfig

**Description:** Complete the existing `infra-gate-mcp-view` RBAC and generate/mount a distinct, single-context kubeconfig for the upstream process. The primary downstream retains its mutation-capable credential; credentials are never shared between descriptors.

**Acceptance criteria:**

- [x] The viewer Role permits only the resources needed by the approved read tools, including `get` on `pods/log`, and grants no Secret or mutation verbs.
- [x] Gateway composition passes the viewer kubeconfig only to the Kubernetes MCP descriptor and the primary kubeconfig only to the primary descriptor.
- [x] A missing/unreadable secondary kubeconfig, a shared normalized primary path, multiple contexts, or a current-context mismatch fails configuration validation when the secondary is enabled.

**Verification:** Run live `rtk kubectl auth can-i` assertions for allowed pod list/get and `pods/log` reads plus denied Secret/create/patch/delete actions, then run the focused configuration tests.

**Dependencies:** Task 1.

**Files likely touched:** `deploy/minikube/rbac.yaml`, the existing demo kubeconfig generation script under `scripts/`, `src/InfraGate.McpGateway/Configuration/KubernetesMcpServerProcessOptions.cs`, and corresponding configuration tests.

**Estimated scope:** Medium (3–5 files).

#### Task 3: Generate a fixed, namespaced upstream configuration

**Description:** Change the run-profile projection to produce a core-only, stateless TOML configuration with exact tools, pair it with the dedicated single-context kubeconfig, and project the required namespace allowlist to the Gateway. Replace `pods_list` with `pods_list_in_namespace` and remove `resources_get`.

**Acceptance criteria:**

- [x] Generated TOML enables only the reviewed tool names and disables multi-cluster/default discovery behavior; configured process arguments are exactly `--config <path>` and sibling TOML drop-ins are rejected.
- [x] Namespace allowlist and context are mandatory when the secondary is enabled; wildcard/empty namespaces, missing kubeconfigs, multi-context kubeconfigs, and current-context mismatch fail validation.
- [x] Run-profile tests prove `pods_list` and `resources_get` are absent and `pods_list_in_namespace` is present.

**Verification:** Run `rtk dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj --filter KubernetesMcpServer` and inspect the generated TOML fixture.

**Dependencies:** Tasks 1–2.

**Files likely touched:** `deploy/run-profiles.yaml`, `src/InfraGate.RunProfiles/Profiles/KubernetesMcpServerProfile.cs`, `src/InfraGate.RunProfiles/Rendering/TomlFileRenderer.cs`, and focused RunProfiles tests.

**Estimated scope:** Medium (3–5 files).

### Checkpoint A: Trust Boundary (after Tasks 1–3)

- [x] ADRs, runtime profile, RBAC, and kubeconfig wiring describe the same tool and namespace boundary.
- [x] Live authorization proves the viewer can read the approved pod resources and logs but cannot mutate or read Secrets.
- [x] Primary mutation routing and credentials are unchanged.
- [ ] Human review confirms the implemented namespace, initial three-tool set, and response limit.

### Phase 2: Enforce Kubernetes Read Policy in the Gateway

#### Task 4: Add an exact source-specific request policy

**Description:** Introduce a small Gateway-owned policy for the Kubernetes MCP source. Validate exact tool name, required namespace, allowed arguments, and per-tool bounds before dispatch; validate the configured context against the dedicated single-context kubeconfig at startup. Do not use downstream annotations as authorization.

**Acceptance criteria:**

- [x] A secondary call is dispatched only when its source and exact tool name match the approved policy.
- [x] Namespace/context-override escape, unknown arguments, missing log tail, tail above 200, and raw-resource/kind attempts return typed MCP errors before downstream invocation.
- [x] Primary source policy and existing mutation approval gates remain independently enforced.

**Verification:** Run focused policy and `GatewayToolDispatcher` tests covering every allow/deny branch and asserting denied calls never reach the hand-written fake downstream.

**Dependencies:** Task 3.

**Files likely touched:** a new policy type under `src/InfraGate.McpGateway/Guardrails/` or `McpTransport/Dispatch/`, `GatewayToolDispatcher.cs`, `ConfigurationExtensions.cs`, and focused unit tests.

**Estimated scope:** Medium (4–5 files).

#### Task 5: Bound and safely project secondary responses

**Description:** Apply source-aware output bounds to the final serialized model-visible envelope after MCP parsing and sanitization, and keep raw-object access disabled. Add a dedicated projection policy path for any future ConfigMap support rather than generic passthrough.

**Acceptance criteria:**

- [x] The complete secondary model-visible envelope cannot exceed the validated 256 KiB ceiling and produces a typed error/audit signal when rejected.
- [x] Tests prove `resources_get` Secret and ConfigMap attempts are rejected before dispatch and cannot expose downstream fixtures.
- [x] Normal pod and bounded log responses remain useful after sanitization.

**Verification:** Run focused policy/sanitization tests with boundary-size, Secret, ConfigMap, prompt-injection, and ordinary response fixtures.

**Dependencies:** Task 4.

**Files likely touched:** the new Kubernetes response policy, `GuardedToolRunner.cs`, `PromptInjectionGuard.Sanitization.cs`, and their focused tests.

**Estimated scope:** Medium (3–5 files).

### Checkpoint B: Policy Enforcement (after Tasks 4–5)

- [x] Exact source/name/argument checks precede every secondary call.
- [x] Log, namespace, raw-resource denial, and final-envelope byte bounds have positive and negative tests; unbounded event listing is excluded.
- [x] Defense-in-depth tests prove a future RBAC mistake would still not expose raw Secrets through the Gateway.

### Phase 3: Preserve MCP Result Semantics

#### Task 6: Replace the text-only downstream call contract

**Description:** Introduce a typed downstream call result that preserves MCP content blocks, `structuredContent`, `isError`, and relevant `_meta`. Keep transport exceptions distinct from valid MCP error results.

**Acceptance criteria:**

- [x] `IDownstreamMcpClient` returns a typed result containing all downstream MCP result fields needed by the Gateway.
- [x] Text, multiple content blocks, structured content, metadata, MCP errors, and transport errors have separate unit coverage.
- [x] No successful downstream result is flattened to text before policy and sanitization.

**Verification:** Run `rtk dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter DownstreamMcpClient`.

**Dependencies:** Task 4.

**Files likely touched:** `McpTransport/Client/IDownstreamMcpClient.cs`, `McpTransport/Client/DownstreamMcpClient.cs`, a new result record in the same folder, and `DownstreamMcpClientTests.cs`.

**Estimated scope:** Medium (4 files).

#### Task 7: Sanitize typed content without destroying its shape

**Description:** Adapt guardrail processing to sanitize each textual content block and recursively redact string values in structured content while preserving block order and result error state. Unsupported or over-limit content types from the secondary fail closed.

**Acceptance criteria:**

- [x] Sanitization preserves safe multi-block and structured results while redacting sensitive or injected text at any supported nesting level.
- [x] `isError` remains unchanged through sanitization, and an unsupported secondary content type becomes an explicit policy error.
- [x] Audit events retain source, tool, direction, finding category, and resolved identity without storing the sensitive payload.

**Verification:** Run focused `GuardedToolRunner` and prompt-injection/sensitive-redaction tests over typed fixtures.

**Dependencies:** Tasks 5–6.

**Files likely touched:** `GuardedToolRunner.cs`, typed sanitization result records, `PromptInjectionGuard.Sanitization.cs`, and focused tests.

**Estimated scope:** Medium (3–5 files).

#### Task 8: Emit correct Gateway MCP results and envelopes

**Description:** Carry the typed result through dispatch and Gateway MCP response creation. Model-visible status must derive from the final MCP error state; exceptions, policy denial, downstream `isError`, and success must remain distinguishable.

**Acceptance criteria:**

- [x] Gateway `tools/call` preserves safe content, structured content, metadata, and `isError` end to end.
- [x] Downstream/tool/transport/policy failures cannot produce `status: success` or `isError: false`.
- [x] Existing primary tool response behavior remains compatible, with explicit regression tests for approval and read flows.

**Verification:** Run focused `GatewayToolDispatcherTests` plus Gateway HTTP MCP integration tests for successful and failing calls.

**Dependencies:** Tasks 6–7.

**Files likely touched:** `McpTransport/GatewayToolDispatcher.cs`, `McpTransport/Dispatch/IGatewayToolDispatcher.cs`, Gateway response mapping code, and their tests.

**Estimated scope:** Medium (3–5 files).

### Checkpoint C: MCP Semantics (after Tasks 6–8)

- [x] Contract tests cover every MCP result field the downstream SDK exposes.
- [x] A real or protocol-faithful downstream `isError: true` reaches the client as an error without losing sanitized diagnostic content.
- [x] Primary approval-flow regression tests still pass.

### Phase 4: Make Federation Deterministic and Failure-Isolated

#### Task 9: Represent one catalog with immutable source ownership

**Description:** Replace ad hoc merging with catalog entries that carry source identity, expected name/schema, and policy classification. Validate each source snapshot before publication.

**Acceptance criteria:**

- [x] Every exposed tool maps to exactly one configured source and its reviewed input schema.
- [x] Duplicate names, schema drift, forbidden annotations, or unexpected tools reject the entire optional secondary snapshot and record a degraded reason.
- [x] Calls route through the published catalog entry, not a second independent name lookup.

**Verification:** Run registry and dispatcher tests for valid federation, duplicate names, schema mismatches, unknown tools, and correct source routing.

**Dependencies:** Tasks 4 and 8.

**Files likely touched:** `McpTransport/Dispatch/DownstreamToolRegistry.cs`, a new catalog entry/snapshot type, `GatewayToolDispatcher.cs`, and focused tests.

**Estimated scope:** Medium (4–5 files).

#### Task 10: Isolate an optional secondary catalog failure

**Description:** Make primary catalog publication mandatory and secondary publication optional. A secondary list timeout, malformed response, collision, or unavailable process must omit all secondary entries without failing primary `tools/list`.

**Acceptance criteria:**

- [x] Primary tools remain listable and callable for every covered secondary failure mode.
- [x] A failed secondary snapshot is never partially published or served from an unvalidated list.
- [x] Health and logs expose a stable degraded reason without leaking credentials or response bodies.

**Verification:** Run `GatewayToolDispatcherTests` and Gateway HTTP integration tests with secondary unavailable, timing out, colliding, and returning malformed catalog data.

**Dependencies:** Task 9.

**Files likely touched:** `DownstreamToolRegistry.cs`, Gateway catalog/list dispatch code, health state interface, and tests.

**Estimated scope:** Medium (3–5 files).

#### Task 11: Tie catalog generation to child-process generation

**Description:** Keep the catalog intentionally immutable while a downstream process is healthy. On a supervised restart, list and validate the replacement process's tools once and atomically swap the source snapshot; retain primary tools throughout.

**Acceptance criteria:**

- [x] Concurrent callers observe either the old valid source generation or the new valid generation, never a partially initialized catalog.
- [x] A restarted secondary cannot serve calls until its replacement catalog passes validation.
- [x] No polling or `tools/list_changed` implementation is introduced.

**Verification:** Run deterministic concurrency tests around restart, validation failure, and snapshot replacement.

**Dependencies:** Tasks 9–10; the lifecycle callback is completed with Task 12.

**Files likely touched:** `DownstreamToolRegistry.cs`, the downstream lifecycle abstraction, and registry concurrency tests.

**Estimated scope:** Medium (3–4 files).

### Checkpoint D: Catalog Federation (after Tasks 9–11)

- [x] One catalog is the routing authority for list and call operations.
- [x] Optional-secondary failure never removes or blocks primary tools.
- [x] Collision/schema/restart behavior is deterministic and covered by concurrency tests.

### Phase 5: Supervise and Observe the Secondary

#### Task 12: Add bounded child-process recovery

**Description:** Introduce the smallest sufficient lifecycle supervisor around downstream stdio creation. Detect unexpected exit or broken transport, perform single-flight restarts with capped exponential backoff and jitter, and expose the current process generation.

**Acceptance criteria:**

- [x] Concurrent failures trigger only one restart attempt, with configured lower/upper backoff bounds and cancellation on Gateway shutdown.
- [x] A restart creates a fresh transport/session and notifies the catalog lifecycle; stale clients cannot receive new calls.
- [x] Exhausted attempts leave the optional secondary degraded without taking down the primary downstream or Gateway.

**Verification:** Use a real controllable subprocess fixture (not a mocking package) to test exit, broken pipe, concurrent calls, backoff, recovery, and shutdown.

**Dependencies:** Task 11.

**Files likely touched:** `McpTransport/Client/DownstreamMcpClient.cs`, a small supervisor/lifecycle type, `ConfigurationExtensions.cs`, and integration tests.

**Estimated scope:** Medium (4–5 files).

#### Task 13: Add downstream-aware health and readiness details

**Description:** Extend health reporting beyond PostgreSQL. Primary downstream health remains required; the optional Kubernetes source reports ready, starting, backing off, or degraded, with its last validated catalog generation.

**Acceptance criteria:**

- [x] Health output distinguishes mandatory primary failure from optional secondary degradation.
- [x] Secondary readiness requires a live transport plus a validated catalog for the current process generation.
- [x] Health details contain no kubeconfig paths, tokens, arguments, or downstream payloads.

**Verification:** Run health endpoint tests for startup, healthy, restart, catalog rejection, exhausted recovery, and shutdown states.

**Dependencies:** Tasks 10–12.

**Files likely touched:** Gateway health registration/check types, `ConfigurationExtensions.cs`, health endpoint tests, and Gateway README health documentation.

**Estimated scope:** Medium (3–5 files).

#### Task 14: Add source telemetry and W3C trace propagation

**Description:** Instrument catalog initialization, calls, failures, latency, policy denials, output rejection, restarts, and degraded duration with bounded source/tool tags. Inject current `traceparent`/`tracestate` into downstream MCP request `_meta` while preserving allowed existing metadata.

**Acceptance criteria:**

- [x] Metrics/logs identify primary versus Kubernetes source and stable tool names without high-cardinality arguments or payload data.
- [x] Downstream calls contain valid W3C trace context in MCP `_meta` and retain the same trace across Gateway policy/dispatch spans.
- [x] Telemetry tests verify success, MCP error, transport error, policy denial, restart, and catalog rejection signals.

**Verification:** Run focused observability tests using an in-memory listener/exporter and a real protocol fixture for request metadata.

**Dependencies:** Tasks 6, 9, and 12–13.

**Files likely touched:** Gateway telemetry conventions/instrumentation, `DownstreamMcpClient.cs`, process supervisor, and observability tests.

**Estimated scope:** Medium (4–5 files).

### Checkpoint E: Runtime Reliability (after Tasks 12–14)

- [x] Killing the real secondary process produces a bounded degraded interval, preserves primary reads/mutations, and recovers a newly validated catalog.
- [x] Health, logs, metrics, and traces explain the failure and recovery without sensitive data.
- [x] Shutdown leaves no orphan child process.

### Phase 6: Make Acquisition a Verified Adapter Bundle

#### Task 15: Consolidate the upstream version and checksum manifest

**Description:** Replace duplicated version pins with one checked-in adapter manifest consumed by the installer and container build. Download official release assets, verify per-platform SHA-256 before installation, and assert the binary-reported version.

**Acceptance criteria:**

- [x] The reviewed linux-amd64 entry pins `v0.0.66` and SHA-256 `692a7b283a96140311fd46f13b8373657b2e9bfe660a36bb6434e8c42d899dbc`.
- [x] Local and Docker acquisition use the same manifest/installer path; `go install` and the duplicate Dockerfile pin are removed.
- [x] Checksum mismatch, unsupported platform, or reported-version mismatch fails before the binary is used.

**Verification:** Run installer tests for success and each failure mode, then execute `rtk proxy .tools/bin/kubernetes-mcp-server --version` on the installed artifact and verify `v0.0.66`.

**Dependencies:** Task 1.

**Files likely touched:** a new version/checksum manifest under `eng/` or `scripts/`, `scripts/install-kubernetes-mcp-server.sh`, `deploy/docker/mcp-gateway.Dockerfile`, and installer tests.

**Estimated scope:** Medium (4 files).

#### Task 16: Validate production composition as one adapter bundle

**Description:** Wire the verified binary, fixed TOML, viewer kubeconfig, source policy, process descriptor, and lifecycle supervisor through the same production DI/configuration path. Validate compatibility as a bundle at startup.

**Acceptance criteria:**

- [x] Enabling the secondary through real configuration resolves exactly one Kubernetes downstream using the verified executable, TOML, working directory, and viewer credential.
- [x] Startup validation rejects missing files, unexpected versions, writable/ambiguous credential selection, and profile/policy tool mismatches.
- [x] Disabling the secondary leaves the primary graph unchanged and creates no child process.

**Verification:** Run `GatewayDiWiringTests`, `KubernetesMcpServerProcessOptionsTests`, `DownstreamProcessDescriptorTests`, and a container configuration smoke test.

**Dependencies:** Tasks 2–5, 9, 12, and 15.

**Files likely touched:** `Configuration/ConfigurationExtensions.cs`, `Configuration/KubernetesMcpServerProcessOptions.cs`, `McpTransport/Client/DownstreamProcessDescriptor.cs`, and their tests.

**Estimated scope:** Medium (4–5 files).

### Checkpoint F: Reproducible Bundle (after Tasks 15–16)

- [x] Local and container installs yield the same verified upstream version.
- [x] The generated profile, source policy, descriptor, and live `tools/list` agree exactly.
- [x] The Gateway can still start with the integration disabled.

### Phase 7: Prove the Real Integration and Put It in CI

#### Task 17: Replace the weak real-binary test with a production-path contract test

**Description:** Rework the existing Kubernetes MCP integration test to start the Gateway through production composition, install the pinned binary, use the dedicated viewer kubeconfig, and call the correct namespaced tool against a disposable real cluster.

**Acceptance criteria:**

- [x] The test calls `pods_list_in_namespace` with its actual `v0.0.66` schema, asserts `isError == false`, and proves a known pod from the allowed namespace is returned.
- [x] The same suite proves cluster-wide list, Secret/raw-resource read, mutation, namespace escape, absent/oversized log tail, and oversized result are denied.
- [x] The test fails with a clear prerequisite error in required CI mode; local opt-in skipping remains explicit and reported rather than silently passing.

**Verification:** Run the focused integration test against the repository's real Kubernetes test environment and capture the successful tool result plus live RBAC denial output.

**Dependencies:** Tasks 3–16.

**Files likely touched:** `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayKubernetesMcpServerIntegrationTests.cs`, its real-cluster fixture/bootstrap, and test README documentation.

**Estimated scope:** Medium (2–4 files).

#### Task 18: Add a required automated integration workflow

**Description:** Add a CI job that provisions the supported disposable cluster, installs the checksum-verified binary, applies viewer RBAC and a known workload, and runs the real integration contract. Preserve a separately labelled manual/self-hosted path only if it exercises additional environments.

**Acceptance criteria:**

- [~] Pull requests that change integration, Gateway transport/policy, RBAC, run profiles, installer, or container files run the real contract job. Path-filtered `pull_request`/`push` triggers are implemented and validated (anchor/alias path lists parse and match correctly when uncommented) but left commented out: the job was migrated off the self-hosted "vaporwave" runner (deregistered entirely — `gh api repos/:owner/:repo/actions/runners` returns zero runners, not merely one offline) to `ubuntu-latest`, per `.agents/Plans/2026-08-14-migrate-integration-tests-ci-to-hosted-runner.md`. Triggers stay commented out until a couple of manual `workflow_dispatch` runs confirm the migrated job is green.
- [x] Missing binary, checksum mismatch, absent cluster, or skipped test fails the job. Gateway integration step switched to `INFRA_GATE_REQUIRE_GATEWAY_INTEGRATION=1` (fails loudly instead of skipping); verified locally that missing-binary fails with a clear message, and `install-kubernetes-mcp-server.tests.sh` (now also run as a CI step) verified passing — its four cases already prove checksum-mismatch/unsupported-platform/version-mismatch/absent-cluster fail closed.
- [x] CI uploads bounded diagnostics sufficient to distinguish acquisition, RBAC, process, policy, and protocol failures without uploading credentials. Added a `Collect diagnostics`/`Upload diagnostics` step pair (`if: failure()`, 7-day retention) capturing pod/resource state, describe output, events, RBAC role/rolebinding specs, a live `kubectl auth can-i --list` for the viewer SA, minikube logs, and the dotnet test TRX files. Verified locally against the real cluster: RBAC output shows only the scoped `pods`/`pods/log` get/list grants with no tokens/certs present.

**Verification:** Run the workflow locally where supported, then confirm one hosted CI execution provisions the cluster and reports a non-skipped successful `pods_list_in_namespace` contract. **Not yet done**: everything reproducible without a live runner (installer fail-closed gates, TRX/diagnostics collection, REQUIRE-mode fail-fast, the underlying contract test itself) was run and verified locally; the hosted-CI-execution half now requires a live `workflow_dispatch` run of the migrated `ubuntu-latest` workflow (`.agents/Plans/2026-08-14-migrate-integration-tests-ci-to-hosted-runner.md`), which has not yet happened.

**Dependencies:** Tasks 15–17.

**Files likely touched:** the applicable `.github/workflows/*.yml`, cluster bootstrap script, and integration test documentation.

**Estimated scope:** Medium (2–4 files).

### Checkpoint G: Real Evidence (after Tasks 17–18)

- [ ] A hosted CI run proves the exact official binary, real viewer RBAC, production DI path, and successful read. **Pending**, not blocked: the self-hosted runner dependency has been removed — `integration-tests.yml` now runs on `ubuntu-latest` with its own disposable minikube cluster and demo workload (`.agents/Plans/2026-08-14-migrate-integration-tests-ci-to-hosted-runner.md`) — but a live validated run hasn't happened yet. The equivalent evidence was captured via real local execution instead (see Task 17's captured `pods_list_in_namespace` output against the live minikube cluster) — this satisfies everything the checkpoint is protecting against except the "hosted" delivery mechanism itself, pending that live run.
- [x] Negative contracts prove all intended fail-closed boundaries. All 6 of Task 17's negative-path assertions (unknown/non-curated tool, namespace escape, absent/oversized log tail) plus Task 18's REQUIRE-mode fail-fast and the installer's 3 fail-closed cases were run for real and passed.
- [x] No integration success depends only on a hand-written fake or a silent skip. The positive-path result comes from a real subprocess talking to a real cluster through the real production DI composition (not `FakeDownstream`), and CI now uses `INFRA_GATE_REQUIRE_GATEWAY_INTEGRATION=1`, which fails the job instead of skipping when prerequisites are missing.

### Phase 8: Project One Agent Diagnostic-Read Profile

#### Task 19: Define the shared diagnostic capability projection

**Description:** Create one exact-name profile for model-visible diagnostic reads. Use it to filter Gateway-discovered MCP tools for agents and to construct agent guardrail policy inputs; keep per-agent mutation/proposal additions explicit.

**Acceptance criteria:**

- [x] The profile includes only reviewed primary diagnostic reads and the approved Kubernetes secondary tools. `DiagnosticCapabilityProfile.ProfiledTools` pins 8 primary reads (`InfraGate.McpServer.KubernetesConventions.ToolNames`) and 3 approved secondary tools (`InfraGate.McpGateway.McpGatewayConventions.SecondaryDownstream.ApprovedTools`), each with its exact expected input-property set.
- [x] `AgentMcpToolset` does not authorize arbitrary tools solely because `ReadOnlyHint` is true. `DiagnosticCapabilityProfile.IsAuthorized` requires the tool name to be in `ProfiledTools` AND its declared schema properties to match the pinned set, in addition to `ReadOnlyHint == true`.
- [x] Unknown, destructive, schema-drifted, or unprofiled tools are excluded with a stable diagnostic reason. `DiagnosticCapabilityExclusionReason` enumerates `NotReadOnly` (destructive/mutation), `NotProfiled` (unknown or reviewed-but-excluded dry-run/diff/check tools), and `SchemaDrifted`; `AgentMcpToolset.ListToolsFilteredAsync` logs the reason per excluded tool.

**Verification:** Ran `AgentMcpToolsetTests` (9/9 passed, including new adversarial cases `GetAgentToolsAsync_WhenConnected_ExcludesReadOnlyToolsNotInProfile` and `GetAgentToolsAsync_WhenConnected_ExcludesSchemaDriftedTools`) against real SDK-constructed `McpClientTool` instances via `InProcessMcpServerFixture`'s in-process HTTP MCP transport with adversarial tool descriptors (unprofiled read-only, profiled-but-schema-drifted, non-read-only mutation). Full solution build and full-solution `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak"` both green with no regressions.

**Dependencies:** Tasks 3–5 and 9.

**Files likely touched:** `src/InfraGate.AgentMcp/AgentMcpToolset.cs`, a new diagnostic profile type, and `tests/InfraGate.AgentMcp.Tests/`.

**Estimated scope:** Medium (3–4 files).

#### Task 20: Wire Observer and Planner guardrails from the profile

**Description:** Replace hard-coded incomplete tool-name sets in Observer and Planner composition with the shared diagnostic projection plus each agent's explicit additional capabilities. Preserve all existing Tool-Call Guardrail middleware.

**Acceptance criteria:**

- [x] Observer can call approved diagnostic reads but no mutation or plan tools beyond its existing contract. `src/InfraGate.Observer/Program.cs`'s `AgentGuardrailPolicy` registration now builds directly from `DiagnosticCapabilityProfile.ToolNames` (replacing the hard-coded 8-name `ObserverConventions.ToolNames.*` enumeration), so Observer's LLM-facing guardrail and cross-agent inbound whitelist (`ObserverInboundAgentHandler`) can never drift from what `AgentMcpToolset` actually discovers.
- [x] Planner can call approved diagnostic reads and its existing plan-proposal/status tools, while upstream mutations remain blocked. `src/InfraGate.Planner/Program.cs`'s `AgentGuardrailPolicy` registration now builds from `DiagnosticCapabilityProfile.ToolNames` plus its one genuine explicit additional capability, `AskObserverTool.FunctionName` (`ask_observer_to_inspect`) — replacing a previous hard-coded list, 5 of whose 8 entries (`get_k8s_pods`, `describe_k8s_resource`, `get_k8s_deployments`, `get_k8s_services`, `get_k8s_endpoints`) named tools that do not exist anywhere in the real system, meaning real profiled reads (e.g. `get_k8s_resource`, `get_deployment_diagnostics`) were previously always blocked for Planner. `propose_plan`/`get_plan_status` remain correctly excluded from the allow-list (they're invoked deterministically via `CallToolAsync`, never offered to the LLM). The now-fully-unused fictional constants were removed from `PlannerConventions.ToolNames`.
- [x] Agent tests prove approved upstream reads execute and a newly discovered unprofiled read-only tool is still blocked. New `tests/InfraGate.AgentGuardrails.Tests/UnitTests/DiagnosticCapabilityGuardrailPolicyTests.cs` builds the exact same `AgentGuardrailPolicy` expressions Observer's and Planner's `Program.cs` now use and drives them through the real `ToolCallGuardrailExtensions.UseToolCallGuardrail` middleware (not a re-implementation): every one of the 11 real `DiagnosticCapabilityProfile.ToolNames` executes for both agents (22 theory cases); the known unprofiled adversarial name `get_k8s_pods` (matching `InProcessMcpServerFixture.UnprofiledReadOnlyToolName` in `InfraGate.AgentMcp.Tests`) stays blocked for both; `propose_plan` stays blocked for both; `ask_observer_to_inspect` is allowed for Planner and blocked for Observer. 28 new tests, all passing.

**Verification:** Ran focused `InfraGate.AgentGuardrails.Tests` (75/75, incl. 28 new), `InfraGate.Observer.Tests` (310/310), `InfraGate.Observer.IntegrationTests` (3/3), `InfraGate.Planner.Tests` (226/226), `InfraGate.Planner.IntegrationTests` (23/23), and `InfraGate.AgentMcp.Tests` (9/9) — all green. Then ran the full-solution build (`dotnet build InfraGate.slnx`: 56 projects, 0 errors, 0 warnings) and full-solution test (`dotnet test InfraGate.slnx --filter "Category!=Keycloak"`: all 29 test-project assemblies report `Passed!`/`Failed: 0`, 836/836 on the largest, `InfraGate.McpGateway.Tests`) — no regressions anywhere in the solution.

**Dependencies:** Task 19 and the successful real contract in Task 17.

**Files likely touched:** `src/InfraGate.AgentGuardrails/AgentGuardrailPolicy.cs`, `src/InfraGate.Observer/ObserverConventions.cs` or composition, `src/InfraGate.Planner/PlannerConventions.cs` or composition, and focused tests.

**Estimated scope:** Medium (4–5 files).

### Checkpoint H: Agent Consumption (after Tasks 19–20)

- [x] Observer and Planner tool discovery and enforcement use the same diagnostic profile. `AgentMcpToolset.ListToolsFilteredAsync` (discovery) and both agents' `AgentGuardrailPolicy` registrations (enforcement) now all derive from the single `DiagnosticCapabilityProfile` in `InfraGate.AgentMcp` — Planner's registration additionally unions in only its one genuine extra capability, `ask_observer_to_inspect`.
- [x] The real namespaced upstream read reaches each intended agent path without weakening mutation guards. Task 17's live-cluster contract test already proves the discovery path end-to-end; `DiagnosticCapabilityGuardrailPolicyTests` proves every profiled name additionally clears the enforcement gate for both agents, while `propose_plan` and every Destructive=true tool remain excluded from discovery (Task 19) and blocked by enforcement (Task 20) in both agents.
- [x] An annotation-only or unexpected tool remains unavailable. `get_k8s_pods` (ReadOnlyHint=true, not profiled) is excluded from discovery (`AgentMcpToolsetTests.GetAgentToolsAsync_WhenConnected_ExcludesReadOnlyToolsNotInProfile`) and independently still blocked by enforcement even if offered anyway (`DiagnosticCapabilityGuardrailPolicyTests.*UnprofiledReadOnlyTool_RemainsBlocked`), for both agents.

### Phase 9: Define—but Do Not Begin—the Full Upstream Swap

#### Task 21: Codify the mutation evidence-parity contract

**Description:** Create a reviewable matrix and real conformance-test specification for every capability required before replacing `InfraGate.McpServer`: create, update, delete, scale, restart, set-image, dry-run validation, diff, resourceVersion freshness, canonical plan digest binding, approval grant verification, audit identity/events, failure semantics, and rollback behavior.

**Acceptance criteria:**

- [x] Every current mutation operation maps to required input, preview/diff, freshness evidence, approval binding, execution output, audit events, and negative tests. `docs/mutation-evidence-parity-contract.md` §1 is the matrix (Apply/Delete/Scale/Restart/Set-Image rows), verified against the actual `FreshnessPolicy` composition in `KubernetesBuilderInfrastructure.BuildFreshnessPolicy` and the 5-step Gate 7/8 order in `KubernetesPlanExecutor.CheckPreExecutionAsync`, not just the conventions constants.
- [x] A candidate upstream release must pass the contract through real Kubernetes and OAuth/approval infrastructure; roadmap statements or annotations are not evidence. §3's conformance-test specification requires reproducing the `InfraGate.Safety.E2E.Tests` real-Keycloak/real-gateway/real-Kubernetes tier structure, plus 5 net-new per-operation properties; §4 states explicitly that CI passing with skipped real integration does not satisfy the contract.
- [x] Missing capability is represented as `no-go`, with no compatibility shim permitted to bypass the existing pre-execution gate. §4 states the current assessment is `no-go` (ADR-0033's unresolved upstream dry-run-mode question), states the hard floor (any single missing capability keeps the whole result `no-go`), and states the no-shim rule concretely (no synthesized dry-run/diff/freshness result, no skipped Gate 7/8 sub-check, no fabricated audit field).

**Verification:** Cross-review the matrix against ADR-0021, ADR-0033, the mutation-approval glossary/ADRs, primary McpServer tests, and Safety E2E properties.

**Dependencies:** Task 1; this documentation can be developed alongside Phases 2–8 but cannot authorize routing.

**Files likely touched:** a new architecture/evidence document under `docs/`, relevant ADR cross-links, and a future conformance-suite specification under `tests/InfraGate.Safety.E2E.Tests/` documentation.

**Estimated scope:** Medium (2–4 files).

#### Future trigger (not part of this plan's completion ledger): Run a release-based no-go/go assessment

**Description:** A future plan may run the full evidence contract once an upstream release claims the necessary mutation surface, without changing production routing. It must record the tested release/commit, artifacts, failures, security review, and operational rollback result in an ADR amendment. The current assessment remains `no-go`: PR #1026 is unmerged/merge-dirty and covers only create/update dry-run.

**Acceptance criteria:**

- [ ] The assessment uses a released, checksum-pinned artifact and records reproducible real-cluster evidence for every matrix row.
- [ ] Any missing operation, diff/freshness evidence, approval binding, audit behavior, or rollback proof keeps the decision `no-go`.
- [ ] A `go` result still requires explicit human approval and a separate implementation plan before any mutation route or `InfraGate.McpServer` removal changes.

**Verification:** Review the evidence bundle and ADR decision with security/architecture owners; confirm the current routing/configuration diff is empty.

**Trigger:** Task 21 plus a future qualifying upstream release and explicit human authorization for a separate plan.

**Files likely touched:** the evidence matrix, relevant ADR amendment, and real conformance test project only; no production routing files.

**Estimated scope:** Medium (documentation plus separately scoped conformance execution).

### Future Checkpoint I: Replacement Gate (outside this plan's completion ledger)

- [ ] The current upstream state is explicitly recorded as `no-go`.
- [ ] No production mutation routing or primary-server removal is part of this plan's implementation.
- [ ] Any future `go` decision starts a new reviewed plan with rollout and rollback steps.

## Recommended Implementation Order and Parallelism

The critical path is Tasks 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13/14 → 16 → 17 → 18 → 19 → 20. Task 15 can proceed after Task 1 in parallel with Gateway policy work. Task 21 can proceed after Task 1 as an independent documentation/evidence-contract track. Tasks sharing `GatewayToolDispatcher`, `DownstreamMcpClient`, `ConfigurationExtensions`, or production integration fixtures should remain sequential to avoid conflicting contracts.

Implementation should stop for human review at every checkpoint. Confirm the base branch before creating any implementation worktree or commit.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Upstream tool names or schemas change between pinned releases | High | Pin artifact and checksum; validate exact catalog/schema before publication; upgrade only through the real contract workflow. |
| A permissive kubeconfig bypasses intended read-only behavior | High | Separate viewer credential; deny mutation/Secrets in RBAC; independently enforce Gateway source policy; prove both with live negative tests. |
| Logs or resource output leaks sensitive/unbounded data | High | Require bounded `tail`; cap result bytes; disable raw reads; sanitize typed content; audit rejection without payload retention. |
| Typed-result refactor regresses primary approval flows | High | Land result contract before dispatch mapping; retain primary regression tests and Safety E2E checks at the MCP semantics checkpoint. |
| Secondary failure destabilizes Gateway startup or `tools/list` | Medium | Treat the whole secondary snapshot as optional; atomically publish only validated catalogs; expose degradation through health and telemetry. |
| Automatic restart creates storms or stale routing | Medium | Single-flight capped backoff, process generations, fresh catalog validation, cancellation on shutdown, bounded retry telemetry. |
| Agent profile drifts from Gateway exposure | High | One exact-name diagnostic profile feeds discovery and Tool-Call Guardrails; tests reject annotation-only and schema-drifted tools. |
| Official release assets differ by platform | Medium | Manifest keyed by supported OS/architecture with individual checksums; fail unsupported tuples; test container target explicitly. |
| CI remains green because real integration is skipped | High | Required CI mode treats any missing prerequisite or skip as failure and asserts `isError == false` plus known cluster data. |
| Upstream mutation work is mistaken for approval parity | Critical | Formal evidence matrix, current `no-go`, unchanged routing, explicit ADR/human gate, and a separate future implementation plan. |

## Open Questions for Review

1. Is the production namespace scope initially only `mcp-nginx-demo`, or should the required allowlist contain additional named namespaces? Wildcards are not recommended.
2. Are the proposed initial limits—200 log lines and 256 KiB per result—acceptable, or do operational use cases justify lower reviewed values?
3. Should optional-secondary degradation leave the aggregate readiness endpoint ready with a degraded component (recommended), or should selected production profiles opt into strict readiness?
4. Which OS/architecture tuples beyond linux-amd64 must the verified artifact manifest support in the first implementation?
5. Which hosted Kubernetes mechanism should the required workflow standardize on using existing repository bootstrap patterns: Minikube, kind, or another already-supported environment?

## Rollout Guidance

1. Land and validate the trust boundary, RBAC, fixed profile, and verified artifact with the secondary disabled by default outside development.
2. Enable in the disposable real-cluster test environment and require Checkpoints B–G to pass.
3. Enable in a non-production profile for one reviewed namespace; observe policy denials, result sizes, latency, restart rate, and degraded duration.
4. Enable for Observer first, then Planner, only after the diagnostic profile tests and real agent-path checks pass.
5. Promote by profile/environment. Never widen RBAC, namespaces, tool names, or result bounds as an incident workaround.

## Rollback Guidance

- Disable only the optional Kubernetes secondary in the run profile; primary reads, approval planning, and mutation execution remain on the existing InfraGate path.
- Preserve the dedicated viewer RBAC and credential separation during rollback; do not restore the shared mutation-capable kubeconfig.
- Revert to the previously recorded checksum-pinned adapter bundle only if its existing contract suite passes; never bypass version/checksum validation.
- If typed-result changes affect the primary downstream, roll back that deployment as a unit and use the primary regression/Safety E2E evidence to confirm restoration.
- Process supervision must terminate the child and clear its catalog snapshot when disabled so no stale upstream tool remains callable.

## Completion Evidence Required

Implementation is complete only when the focused unit suites, Gateway integration suites, agent suites, runtime-safety regressions, and real-binary Kubernetes CI contract all pass with captured non-skipped output. The handoff must include the verified binary version/checksum, live RBAC allow/deny results, a successful `pods_list_in_namespace` result with `isError == false`, a demonstrated secondary kill/recovery sequence, and confirmation that primary mutation routing did not change.
