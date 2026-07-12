# Plan: kubernetes-mcp-server as a Read-Only Secondary Downstream (Interim Step)

**Date:** 2026-07-12
**Goal:** Run the upstream `containers/kubernetes-mcp-server` Go binary alongside the existing `InfraGate.McpServer`, exposed through the Gateway as a small, curated, **read-only-only** set of tools, acquired via `go install` of a pinned official release at build time — with zero changes to the approval/mutation path.

## Context

This is the interim step of a longer-term direction already recorded in the repo: **ADR-0021** states the active roadmap plans to eventually swap `InfraGate.McpServer` for `containers/kubernetes-mcp-server` entirely. That full swap is deliberately **not** what this plan does — it's blocked on an open question (does kubernetes-mcp-server's generic apply support a dry-run mode `KubernetesPlanBuilder` can consume for its pre-approval diff?) that this plan does not attempt to resolve.

Instead, this plan adds kubernetes-mcp-server as a **second, independent downstream MCP process**, read-only only, never routed through `IDomainAdapter` / `KubernetesPlanBuilder` / `InfraGate.Approvals`. It exists purely to give the AI broader inspection tools (arbitrary resource kinds, etc.) beyond InfraGate.McpServer's narrow Deployment/Service/ConfigMap surface, and to prove the two-downstream architecture works before any larger swap is considered.

### Architecture findings that shape this plan

- **Single-downstream today.** `IDownstreamMcpClient` is registered as exactly one DI singleton (`ConfigurationExtensions.cs:76`), consumed by `DownstreamToolRegistry`, `GuardedToolRunner`, and `SanitizingToolCaller`. There is no multi-source concept anywhere in this chain — adding a second downstream is a genuine (bounded) code change, not a config toggle.
- **Process command is hardcoded to `"dotnet"`.** `McpGatewayConventions.DownstreamProcess.Command` is a shared constant; `DownstreamMcpClient.CreateTransportOptions()` always spawns `dotnet run --project <path>` or `dotnet <assembly>.dll`. Nothing today can spawn an arbitrary binary.
- **`StdioClientTransportOptions` (from the `ModelContextProtocol` SDK) is already command-agnostic** — `Command`, `Arguments`, `WorkingDirectory`, `EnvironmentVariables` — so spawning a Go binary is fully supported by the transport layer; only InfraGate's own conventions/options are .NET-specific.
- **Downstream stdio auth is InfraGate-private.** The bootstrap-line protocol (`BootstrapStdioClientTransport`, `DownstreamStdioBootstrapGate`, `DownstreamAuthConventions.MetaKey`/`BootstrapLineKey`) is only understood by `InfraGate.McpServer`. A stock Go binary must run with `DownstreamAuth.Required = false`, which the existing code path already supports cleanly (`IsDownstreamAuthRequired()` → `StdioClientTransport` with no bootstrap line). This is not a security regression: the Gateway's own documented priority order (`src/InfraGate.McpGateway/README.md`, "Security Controls") already ranks the downstream token *last*, below trusted launch, containment, human approval, and per-action authorization — none of which this plan touches.
- **`KubernetesPolicyValidator`, `KubernetesPlanBuilder`, and the whole Approvals stack are pure and independent of which MCP process executes anything.** This plan does not touch any of them.
- **Production packaging is a multi-stage Docker build** (`deploy/docker/mcp-gateway.Dockerfile`) publishing `InfraGate.McpServer` and `InfraGate.McpGateway` into a `dotnet/aspnet:10.0-noble-chiseled` runtime image — **no shell, no libc tooling** (this is why the repo hand-rolled a C# health-check binary instead of using curl). Any Go binary added to this image must be a static binary (`CGO_ENABLED=0`) or it will not run.
- **Config generation convention:** the repo generates `.env` files from `deploy/run-profiles.yaml` via typed `InfraGate.RunProfiles/Profiles/*.cs` classes (see `DownstreamAuthProfile.cs`, `KubernetesAdapterProfile.cs`). Repo lesson: *"Don't add new … environment-variable mappings by default — prefer generated run-profile JSON configuration."* New config for the secondary downstream should follow this pattern, not ad-hoc env vars. **Important caveat verified during plan review:** `src/InfraGate.RunProfiles/Rendering/` today contains exactly one renderer, `EnvFileRenderer.cs` — there is no existing TOML output anywhere in the project or in `Directory.Packages.props`. Generating a TOML file (Task 4) is a genuinely new capability for this project, not an extension of an existing renderer — see Task 4 for the resulting scope/dependency implications.
- **`DownstreamMcpClient.CreateTransportOptions()` hardcodes more than just the process name.** Verified: `Command` is read from a `public const string` on `McpGatewayConventions.DownstreamProcess` (not instance data), and `Arguments` is built from an `options.DownstreamAssembly`/`options.DownstreamProject` conditional (`DownstreamMcpClient.cs:217-247`). Spawning the Go binary requires rewriting this method to source Command/Arguments/WorkingDirectory from per-instance data — see Task 6.
- **`McpGatewayOptions.ValidateProductionSafety()` (`McpGatewayOptions.cs:103`, invoked once at `ConfigurationExtensions.cs:45`) throws in Production mode unless `DownstreamAuth.Required == true`** on the single shared options instance. This check must never end up applying to the secondary (always-unauthenticated) downstream — see Task 5's decision to use a separate sibling options type rather than extending `McpGatewayOptions` itself.
- **No Go toolchain exists in this repo today** (no `go.mod`, no Go CI step). This plan introduces the repo's first Go dependency.
- **Repo lesson (explicit, already learned the hard way):** *"Don't add a constructor dependency to a gateway service without updating every test fixture that manually registers it in DI"* — named fixtures: `KeycloakTests`, `SafetyE2EFixture`, `GatewayHttpMcpIntegrationTests`, `GatewayDiWiringTests`. This plan has an explicit task for it so it isn't rediscovered the hard way again.
- **Repo lesson:** *"Don't rely on `IsError=False`... run the downstream binary standalone and inspect its stderr"* when debugging — this shapes Task 1 as a manual standalone spike before any code is written.
- Upstream `kubernetes-mcp-server` is Apache-2.0, pre-1.0 (latest observed tag `v0.0.64`, published 2026-07-10), distributed as a Go module at `github.com/containers/kubernetes-mcp-server/cmd/kubernetes-mcp-server`, installable via `go install ...@<pinned-version>`. Confirmed config surface (from upstream `docs/configuration.md`) includes `read_only`, `enabled_tools`/`disabled_tools`, `denied_resources` — documented example: `enabled_tools = ["pods_list", "pods_get", "pods_log"]`. Exact current tool names/config shape must be **re-verified against the pinned binary**, not assumed from docs, before any code is written (pre-1.0 software can drift between the doc snapshot and the pinned release).

## Request

User decisions (resolve all previously open questions):
1. **Acquisition:** `go install` the official upstream binary, pinned to a specific tagged version, at build time (both local dev and the production Docker image) — not a vendored release-binary download, not npm/PyPI, not a separate Docker image.
2. **Tool scope:** a small, basic, hand-picked set of read-only tools for now — enough to prove the architecture works, not a comprehensive tool surface. Candidate list (see Task 1): `pods_list`, `pods_get`, `pods_log`, `events_list`, `resources_list`, `resources_get`.
3. **Config mechanism:** TOML config file (not CLI-flags-only) for `read_only`/`enabled_tools`, generated via the run-profiles convention.
4. **Install location:** leaning `.tools/bin/` (repo-local, predictable path independent of the caller's `GOPATH`), confirmed during Task 2.
5. **CI coverage:** real integration tests against the actual installed binary, not unit-tests-only.
6. **Version pin:** latest stable tag for now (`v0.0.64` as observed during this research, published 2026-07-10) — no automated-update policy yet.

**Acceptance criteria for this plan's implementation:**
- The Gateway can spawn `kubernetes-mcp-server` as a second stdio MCP subprocess, independently of `InfraGate.McpServer`.
- `tools/list` on the Gateway's MCP endpoint returns the existing InfraGate tools **plus** a small curated set of kubernetes-mcp-server read-only tools.
- Calling one of the new tools returns real cluster data from the same demo namespace/kubeconfig the rest of the stack already uses.
- No kubernetes-mcp-server tool is ever reachable through `request_*`/`execute_approved_plan` — destructive tools are excluded both by the binary's own `read_only` config and by a Gateway-side filter (defense in depth).
- Existing tests, DI wiring, and the mutation/approval path are unaffected.
- Local dev and CI/Docker both acquire the binary reproducibly via pinned `go install`.

## Plan

### Phase 0: Feasibility Spike (no code — do this first, fail fast)

- [ ] **Task 1: Verify the pinned binary standalone over stdio**
  **Description:** `go install github.com/containers/kubernetes-mcp-server/cmd/kubernetes-mcp-server@v0.0.64` locally (re-check this is still the latest tag at implementation start; pin whatever is current then). Run it standalone (not through the Gateway) speaking raw MCP JSON-RPC over stdio (same technique the repo already uses for downstream debugging per the existing lesson). Send `initialize` then `tools/list`. Confirm the chosen curated set exists with these exact names, descriptions, and `readOnlyHint`/`destructiveHint` annotations: **`pods_list`, `pods_get`, `pods_log`, `events_list`, `resources_list`, `resources_get`**. If any name doesn't match what the pinned binary actually exposes, adjust the list to the nearest equivalent real tool name — don't force a mismatch. Confirm the TOML schema for `read_only`/`enabled_tools` (config mechanism is already decided — TOML). Confirm the flag/config name for kubeconfig path and whether namespace scoping is supported at this layer, and check what RBAC the existing `.kube/mcp-nginx-demo.compose.config` context actually grants (if it's already scoped to `mcp-nginx-demo` only, cluster-wide tool exposure may be moot).
  **Acceptance criteria:**
  - [ ] Exact confirmed tool names + schemas for the six candidate tools are written down, with any substitutions noted (replacing assumptions in this plan).
  - [ ] Confirmed TOML schema for `read_only`/`enabled_tools`.
  - [ ] Confirmed kubeconfig flag name and behavior against the demo cluster context.
  **Verification:** Manual transcript of the stdio session (or a scratch script) showing `initialize` → `tools/list` → one successful read-only `tools/call`.
  **Dependencies:** None.
  **Files touched:** None (spike only; findings feed into Task 4/5).
  **Estimated scope:** N/A (research).

### Checkpoint A: Spike confirms the binary runs standalone over stdio and exposes a real, predictable read-only tool set matching (or updating) this plan's assumptions. If the pinned version's tool/config surface differs materially from what's documented, stop and reconcile before writing any Gateway code.

### Phase 1: Acquisition & Packaging

- [ ] **Task 2: Add a pinned local-dev install script**
  **Description:** New script under `scripts/` (e.g. `scripts/install-kubernetes-mcp-server.sh`), following existing script conventions (explicit `set -u`-safe variable definitions — see the repo lesson about `GATEWAY_APP_UID` breaking under `set -u`). Installs the pinned version to `.tools/bin/kubernetes-mcp-server` (repo-local, predictable path independent of the caller's `GOPATH` — using `GOBIN=$(pwd)/.tools/bin go install ...@<pinned-version>`), confirming during implementation that this resolves cleanly for both local dev and Docker build contexts. Add `.tools/` to `.gitignore`. Document Go toolchain as a new local prerequisite in root `README.md`.
  **Acceptance criteria:**
  - [ ] Running the script twice is idempotent and produces the same pinned version.
  - [ ] README documents the new Go prerequisite and the install command.
  **Verification:** `./scripts/install-kubernetes-mcp-server.sh && kubernetes-mcp-server --version` (or equivalent) prints the pinned version.
  **Dependencies:** Task 1 (confirms flags/version).
  **Files touched:** `scripts/install-kubernetes-mcp-server.sh`, `README.md`.
  **Estimated scope:** S.

- [ ] **Task 3: Add a Go build stage to the production Docker image**
  **Description:** Extend `deploy/docker/mcp-gateway.Dockerfile` with a `FROM golang:<pinned-tag> AS k8s-mcp-build` stage running `CGO_ENABLED=0 go install github.com/containers/kubernetes-mcp-server/cmd/kubernetes-mcp-server@<pinned-version>`, then `COPY --from=k8s-mcp-build /go/bin/kubernetes-mcp-server /app/k8s-mcp-server/kubernetes-mcp-server` into the chiseled runtime stage. `CGO_ENABLED=0` is required because the final `aspnet:10.0-noble-chiseled` image has no libc/shell — the binary must be statically linked.
  **Acceptance criteria:**
  - [ ] Image builds successfully with the new stage.
  - [ ] `docker run --entrypoint /app/k8s-mcp-server/kubernetes-mcp-server <image> --version` (or `--help`) runs successfully inside the chiseled image (proves static linking works with no shell/libc present).
  **Verification:** Local `docker build` of `deploy/docker/mcp-gateway.Dockerfile` succeeds; the entrypoint-override smoke check above passes.
  **Dependencies:** Task 1.
  **Files touched:** `deploy/docker/mcp-gateway.Dockerfile`.
  **Estimated scope:** S.

- [ ] **Task 4: Add TOML rendering to InfraGate.RunProfiles, then generate the curated-tools config**
  **Description:** Generate a TOML config file following the existing run-profile generation convention: add a `KubernetesMcpServerProfile.cs`-style class under `src/InfraGate.RunProfiles/Profiles/`, wire it into `deploy/run-profiles.yaml`, and have it render the TOML alongside the existing generated `.env` files rather than hand-writing a static config file. The kubeconfig path is passed via whatever mechanism Task 1 confirms (CLI flag or TOML key) pointing at the same demo kubeconfig context already mounted for the primary Kubernetes adapter. Bake in `read_only = true` unconditionally for this phase — not configurable — since nothing downstream is prepared to route mutations through this client. `enabled_tools` is restricted to the six-tool candidate list from Task 1 (adjusted for any name corrections found there), defined in exactly one place (this new profile class) — no second hand-copied list anywhere else in the codebase (see Task 10).

  **New capability, not an extension:** `src/InfraGate.RunProfiles/Rendering/` currently has exactly one renderer, `EnvFileRenderer.cs`, and there is no TOML support anywhere in the project or in `Directory.Packages.props`. This task introduces the project's first TOML output. As part of this task, explicitly decide and record the TOML-writing approach — either add a new centrally-versioned package (e.g. `Tomlyn`) to `Directory.Packages.props`, or hand-roll a minimal writer sufficient for this flat/array-of-tables schema — with the same supply-chain scrutiny this plan already applies to `go install`/GOSUMDB and SHA-pinned GitHub Actions.
  **Acceptance criteria:**
  - [ ] `read_only = true` is present in every generated config with no way to disable it via env var.
  - [ ] `enabled_tools` is restricted to the curated set confirmed in Task 1, defined in exactly one place.
  - [ ] Config generation follows the existing run-profile pattern (no scattered new env vars).
  - [ ] TOML writer choice (new dependency vs. hand-rolled) is decided and recorded; if a new package, it is added to `Directory.Packages.props` with the same version-pinning discipline as existing dependencies.
  **Verification:** Generated TOML inspected manually; running the binary with the generated file returns only the curated tool set in `tools/list`.
  **Dependencies:** Task 1, Task 2.
  **Files touched:** `src/InfraGate.RunProfiles/Rendering/TomlFileRenderer.cs` (new), `src/InfraGate.RunProfiles/Profiles/KubernetesMcpServerProfile.cs` (new), `deploy/run-profiles.yaml`, `Directory.Packages.props` (if a new TOML package is added), generated TOML output.
  **Estimated scope:** M.

### Checkpoint B: Binary is acquired reproducibly in local dev and in the Docker image; it runs standalone (outside the Gateway) with the generated config and exposes exactly the curated read-only tool set. Nothing in the Gateway process has changed yet.

### Phase 2: Gateway Wiring

- [ ] **Task 5: Add a second downstream-process descriptor as a sibling options type, plus the keyed-DI constant**
  **Description:** Add a small **sibling options record** (e.g. `KubernetesMcpServerProcessOptions`) holding the fields needed to spawn the second process: binary path/command, arguments, working directory, and an explicit `AuthRequired` flag defaulted/hardcoded to `false`. **Do not extend `McpGatewayOptions` itself** — see rationale below. Keep this additive — do not change any existing single-downstream field or `McpGatewayConventions.DownstreamProcess` constants used by the primary client. Add the keyed-DI service key (e.g. `"k8sMcpServer"`) as a single named constant on `McpGatewayConventions` (alongside `DownstreamProcess`) — it must never be a raw string literal repeated across registration and resolution call sites (Task 6, Task 7, Task 8).

  **Why a sibling type, not an extension of `McpGatewayOptions`:** `McpGatewayOptions` is already a single record mixing Auth/Approval/Smtp/primary-downstream concerns; bolting a second, unrelated process descriptor onto it compounds that. More concretely, `ValidateProductionSafety()` (`McpGatewayOptions.cs:103`) throws in Production mode unless `DownstreamAuth.Required == true` on the shared options instance — that check must never apply to the secondary (always-unauthenticated) descriptor. A genuinely separate type sidesteps the question instead of requiring a carve-out inside `ValidateProductionSafety`.
  **Acceptance criteria:**
  - [ ] New sibling options type is optional/off-by-default so existing deployments without it are unaffected.
  - [ ] `ValidateProductionSafety` on `McpGatewayOptions` is unchanged and gains no code path that could require `AuthRequired=true` on the secondary descriptor.
  - [ ] The keyed-DI service key exists as exactly one named constant in `McpGatewayConventions`.
  **Verification:** Unit test constructing the new sibling options type with and without configuration present; unit test confirming `McpGatewayOptions.ValidateProductionSafety` behavior is unaffected by the new type's presence/absence.
  **Dependencies:** Task 4.
  **Files touched:** `src/InfraGate.McpGateway/Configuration/KubernetesMcpServerProcessOptions.cs` (new), `src/InfraGate.McpGateway/McpGatewayConventions.cs`.
  **Estimated scope:** S.

- [ ] **Task 6: Generalize `DownstreamMcpClient.CreateTransportOptions()`, then register a second, keyed client/registry/guarded-runner triple**
  **Description:** `CreateTransportOptions()` (`DownstreamMcpClient.cs:217-247`) currently hardcodes `Command = McpGatewayConventions.DownstreamProcess.Command` (a static `"dotnet"` constant) and builds `Arguments` from an `options.DownstreamAssembly`/`options.DownstreamProject` conditional. **Rewrite this method first** so Command/Arguments/WorkingDirectory/auth-required are sourced from an injected per-instance descriptor — the Task 5 sibling type for the secondary client, and the existing `McpGatewayOptions` fields (wrapped in the same descriptor shape) for the primary. This is the genuine, non-trivial part of "generalizing" the client — it changes the constructor/behavior of a shared internal class, not just its DI registration, and should not be treated as incidental to the steps below.

  Once generalized, reuse `DownstreamMcpClient`, `DownstreamToolRegistry`, and `GuardedToolRunner` as-is for both instances — do **not** build a generic N-way multi-downstream abstraction. Register a second instance of each via .NET keyed DI services (`AddKeyedSingleton`), keyed by the constant added in Task 5 (never a raw string literal at the registration call site), explicitly wired with `AuthRequired = false` and a code comment explaining why (InfraGate-private bootstrap protocol; trust relies on trusted launch + process containment instead — matches the Gateway's documented security-priority order).
  **Architecture decision:** Duplicate the existing single-downstream plumbing as a second parallel keyed set (same classes, different constructor data), rather than generalizing `DownstreamToolRegistry`/`GuardedToolRunner` to handle N sources. Rationale: the ask is specifically "one more, alongside" — not a general framework — and AGENTS.md's simplicity principle disfavors speculative configurability beyond what's requested.
  **Acceptance criteria:**
  - [ ] Existing (unkeyed) `IDownstreamMcpClient` registration, its consumers, and its observable transport behavior (Command/Arguments for the primary dotnet process) are unchanged after the rewrite.
  - [ ] `CreateTransportOptions()` no longer reads `Command` from a static constant for either instance — both come from per-instance descriptor data.
  - [ ] New keyed registrations resolve cleanly at startup with the new client pointed at the Go binary, using the Task 5 constant (no raw string literal at the registration or resolution call site).
  **Verification:** `GatewayDiWiringTests` (extended) resolves both the default and keyed services without error; existing `DownstreamMcpClient` unit tests covering the primary (dotnet) transport-options path still pass unchanged after the rewrite.
  **Dependencies:** Task 5.
  **Files touched:** `src/InfraGate.McpGateway/Configuration/ConfigurationExtensions.cs`, `src/InfraGate.McpGateway/McpTransport/Client/DownstreamMcpClient.cs`.
  **Estimated scope:** M–L.

- [ ] **Task 7: Route `GatewayToolDispatcher` to the secondary read-only source**
  **Description:** Inject the keyed secondary `DownstreamToolRegistry` and `GuardedToolRunner` into `GatewayToolDispatcher`. In `ListToolsAsync`, append the secondary registry's `GetReadOnlyAsync()` tools (via the existing `ToolDefinitionFactory.CreateForwardedTool`). In `IsReadOnlyToolAsync`, check both registries. In `HandleReadOnlyAsync`, dispatch to whichever `GuardedToolRunner` instance owns the tool. Deliberately **never** call `GetDestructiveAsync()` on the secondary registry and never build `request_*` wrappers for it — if the binary's `read_only` config were ever misconfigured, the Gateway must still refuse to expose a mutation path for it. `IsDestructiveToolAsync` stays untouched (primary registry only); any attempted mutation against a secondary tool name falls through to the existing "unknown tool" error, which is the correct default-deny behavior.

  **Implementation note (avoid tripled branching):** rather than hardcoding separate named "primary" and "secondary" branches in each of `ListToolsAsync`/`IsReadOnlyToolAsync`/`HandleReadOnlyAsync`, hold the two `(DownstreamToolRegistry, GuardedToolRunner)` pairs as a small internal fixed-size collection (e.g. a 2-element array) and have those three methods iterate it. This preserves Task 6's "exactly two sources, no N-way config" decision while keeping the read-only dispatch logic in one place instead of duplicated three times.
  **Acceptance criteria:**
  - [ ] `tools/list` shows existing InfraGate tools + curated kubernetes-mcp-server tools.
  - [ ] Calling a curated tool returns real data routed through the correct client.
  - [ ] No `request_<k8s-mcp-server-tool>` wrapper is ever generated, even if the upstream binary's annotations claim a tool is destructive.
  - [ ] Primary/secondary read-only dispatch is not implemented as three separate duplicated if/else branches — it iterates a small shared internal collection instead.
  **Verification:** New unit tests in `GatewayToolDispatcherTests` covering merged listing and routing; manual MCP call through the Gateway.
  **Dependencies:** Task 6.
  **Files touched:** `src/InfraGate.McpGateway/McpTransport/GatewayToolDispatcher.cs`.
  **Estimated scope:** M.

- [ ] **Task 8: Update every hand-wired test fixture**
  **Description:** Per the existing repo lesson, add the new constructor dependencies to every place that manually builds the Gateway's DI container for tests, or the MCP SDK will mask the real DI error as a generic "An error occurred invoking" string: `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`, `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`, `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayDiWiringTests.cs`.
  **Acceptance criteria:**
  - [ ] All four fixtures build the Gateway successfully with the new keyed services present (using a fake/no-op secondary client in fixtures that don't need the real binary).
  **Verification:** Full existing test suite still passes after this task.
  **Dependencies:** Task 6, Task 7.
  **Files touched:** the four files above.
  **Estimated scope:** M.

### Checkpoint C: Gateway starts locally with both downstreams wired. `tools/list` via the real MCP endpoint shows the existing InfraGate tools plus the curated kubernetes-mcp-server tools. All pre-existing tests still pass.

### Phase 3: New Tests

- [ ] **Task 9: Unit tests for the secondary client's transport assembly**
  **Description:** Test that the generalized transport-options builder produces `Command`/`Arguments` pointing at the Go binary (not `dotnet`), including the `--config`/TOML-path argument, with no InfraGate bootstrap-auth line attached.
  **Acceptance criteria:** [ ] Tests cover the TOML-config-path argument assembly and confirm no downstream-auth bootstrap line is sent.
  **Verification:** `dotnet test tests/InfraGate.McpGateway.Tests`.
  **Dependencies:** Task 6.
  **Files touched:** `tests/InfraGate.McpGateway.Tests/UnitTests/*` (new test file).
  **Estimated scope:** S.

- [ ] **Task 10: Opt-in integration test against the real binary**
  **Description:** Mirror the existing `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1` opt-in pattern with a new env-gated test that spins up the real installed `kubernetes-mcp-server`, calls the Gateway's `tools/list` and one curated read-only tool end-to-end against the demo kubeconfig, and asserts (a) the response shape, and (b) that no non-curated or destructive tool is ever listed. Per repo policy this must be a real integration test against the real binary — no mocking of the process. The expected-allowlist for assertion (b) must be read from the same generated TOML/profile output as Task 4 (or from the single Task 4 constant, if exposed), **not** a second hand-copied list of the six tool names — otherwise the two can silently drift the next time Task 1's "adjust to nearest equivalent real tool name" clause fires on a version bump.
  **Acceptance criteria:**
  - [ ] Test skips cleanly (not failing) when the binary isn't installed/opted-in.
  - [ ] Test asserts the curated allowlist is exhaustive (fails if an unexpected tool appears), sourced from Task 4's single generated list rather than a duplicated literal.
  **Verification:** `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests` passes locally with the binary installed.
  **Dependencies:** Task 2, Task 7.
  **Files touched:** `tests/InfraGate.McpGateway.Tests/IntegrationTests/*` (new test file).
  **Estimated scope:** M.

- [ ] **Task 11: Run the real integration test in CI**
  **Description:** Add a pinned `actions/setup-go` step (SHA-pinned commit, per the repo's existing supply-chain policy — no floating `@vN` tags) to `.github/workflows/integration-tests.yml`, install the pinned `kubernetes-mcp-server` version via the Task 2 script (or an equivalent CI-specific install step), and run Task 10's integration test unconditionally (not opt-in) in that workflow, since the whole point is real end-to-end coverage rather than a mocked/skipped check.
  **Acceptance criteria:**
  - [ ] `actions/setup-go` is pinned to a commit SHA, not a version tag.
  - [ ] The integration test runs on every CI invocation of this workflow and fails the build on a regression (not skipped/best-effort).
  **Verification:** CI run is green on the feature branch, with the integration test visibly executing (not skipped) in the workflow log.
  **Dependencies:** Task 10.
  **Files touched:** `.github/workflows/integration-tests.yml`.
  **Estimated scope:** S.

### Checkpoint D: Full test suite (existing + new) is green locally; opt-in integration test passes against the demo cluster; CI decision is made and implemented.

### Phase 4: Documentation

- [ ] **Task 12: Update Gateway docs**
  **Description:** Update `src/InfraGate.McpGateway/README.md` ("Runtime Flow" / "Security Controls") and `.agents/skills/infragate-mcp-gateway/SKILL.md` to describe the new supplementary read-only tool source, its trust boundary (trusted-launch + containment, no downstream token), and the curated tool list.
  **Acceptance criteria:** [ ] Docs accurately describe the two-downstream architecture and its guardrail boundary.
  **Verification:** `/skill:verify-readme-docs`-style read-through against the actual implemented code.
  **Dependencies:** Task 7.
  **Files touched:** `src/InfraGate.McpGateway/README.md`, `.agents/skills/infragate-mcp-gateway/SKILL.md`.
  **Estimated scope:** S.

- [ ] **Task 13: Write an ADR**
  **Description:** Record the decision: secondary downstream, read-only-only, unauthenticated-at-stdio (trusted-launch only), `go install`-pinned acquisition, curated tool allowlist, explicitly not the full ADR-0021 swap. Follow the existing ADR format/numbering (next number after the highest existing ADR).
  **Acceptance criteria:** [ ] ADR documents context, decision, rationale, and consequences, including the explicit boundary that this does not yet resolve the dry-run/diff question blocking the full swap.
  **Verification:** Peer review / user review.
  **Dependencies:** Task 6, Task 7.
  **Files touched:** `docs/adr/00NN-kubernetes-mcp-server-readonly-secondary-downstream.md`.
  **Estimated scope:** S.

### Checkpoint E (Final): All acceptance criteria in the Request section are met. Two downstreams run side by side; the second is strictly read-only, isolated from the approval path, reproducibly acquired, tested, and documented. Ready for user review before any follow-on work toward the full ADR-0021 swap.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Pre-1.0 upstream (`v0.0.x`) API/config/tool-name churn between doc snapshot and pinned release | Medium | Task 1 spike verifies against the actual pinned binary before any code is written; re-verify on every version bump |
| `go install` has weaker supply-chain pinning than a checksummed release-binary download (no `DownstreamAssemblyHash`-equivalent verification today) | Medium | Rely on Go module checksum database (GOSUMDB) verification at install time for phase 1; note as a follow-up hardening item if this graduates past proof-of-concept |
| Chiseled runtime image has no shell/libc — a dynamically-linked Go binary would silently fail to start | High | `CGO_ENABLED=0` static build, verified by an explicit entrypoint-override smoke check in Task 3 |
| First Go toolchain dependency in an all-.NET repo (local dev + CI) | Low–Medium | Documented as a new prerequisite (Task 2); CI scope explicitly decided, not silently skipped (Task 11) |
| Test-fixture DI drift masks real startup errors as generic MCP SDK error text | Medium | Explicit Task 8, directly addressing the repo's own documented lesson |
| Namespace/RBAC scope of the new binary's read access unclear until Task 1 | Low | Task 1 spike checks both the binary's own scoping config and the actual RBAC granted by the reused demo kubeconfig context |
| Enabling a second stdio subprocess widens the Gateway's process/attack surface even in read-only mode | Low | `read_only=true` baked in non-configurably (Task 4) + independent Gateway-side filter that never exposes destructive/`request_*` routing for this source (Task 7), as defense in depth |
| `DownstreamMcpClient.CreateTransportOptions()` hardcodes `Command` as a static constant and branches on .NET-specific assembly/project fields — more surgery than plain DI registration | Medium | Explicit rewrite step called out first in Task 6, scoped and estimated (M–L) separately from the DI-registration work |
| `InfraGate.RunProfiles` has no existing TOML renderer or TOML dependency — Task 4 introduces both, which is a new dependency-supply-chain decision, not a config-format extension | Low–Medium | Explicit dependency decision required as part of Task 4's acceptance criteria, reviewed with the same rigor as the plan's `go install`/Actions supply-chain items |
| A second `McpGatewayOptions`-shaped instance could accidentally trip `ValidateProductionSafety()`'s `DownstreamAuth.Required==true` production gate meant only for the primary downstream | Medium | Task 5 uses a separate sibling options type instead of extending `McpGatewayOptions`, so the secondary descriptor is never subject to that validation path |

## Decisions Made (previously open questions)

- **Tool list:** `pods_list`, `pods_get`, `pods_log`, `events_list`, `resources_list`, `resources_get` — Task 1's spike confirms these exact names exist on the pinned binary and substitutes the nearest real equivalent if any don't.
- **Config mechanism:** TOML config file, generated via the run-profiles convention (Task 4).
- **CI coverage:** real integration test against the installed binary, running unconditionally in `integration-tests.yml` (Task 11) — not unit-tests-only, not opt-in-and-skipped.
- **Version pin:** latest stable tag for now — `v0.0.64` as observed during this research (2026-07-10); no Dependabot/Renovate-style auto-update policy yet.
- **Second downstream-process descriptor:** a separate sibling options type (not an extension of `McpGatewayOptions`), added in Task 5 — resolved during plan verification to avoid entangling `ValidateProductionSafety()`'s production-mode `DownstreamAuth.Required` gate with the secondary, always-unauthenticated descriptor.
- **Keyed-DI service key:** a single named constant on `McpGatewayConventions` (Task 5), never a raw string literal at registration/resolution sites.
- **TOML rendering:** decided as part of Task 4, not left implicit — either a new centrally-versioned package (e.g. `Tomlyn`) or a hand-rolled minimal writer, chosen and recorded during that task.

## Remaining Open Questions

- **Install path:** leaning `.tools/bin/kubernetes-mcp-server` (repo-local, predictable, independent of the caller's `GOPATH`) — confirm this resolves cleanly for both local dev and the Docker build context during Task 2; fall back to `$(go env GOPATH)/bin` if `.tools/bin` proves awkward for the Docker COPY step.
- Re-confirm `v0.0.64` is still the latest tag at actual implementation start (this plan was researched 2026-07-12); pin whatever is current then.
- **TOML writer choice:** new package (e.g. `Tomlyn`) vs. hand-rolled minimal writer — left to Task 4 to decide, since it depends on exactly how much of TOML's grammar the generated config actually needs (flat keys + one array-of-tables for `denied_resources`, per the confirmed upstream schema).
