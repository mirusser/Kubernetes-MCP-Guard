# Implementation Plan: Integrating MCP Tools natively into the Framework (Roadmap §3)

**Date:** 2026-05-30
**Roadmap item:** `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` §3 — *"Integrating AI agents with enterprise tools..."*
**Depends on:** §1 (managed agent workflows) — **Done 2026-05-29** — and §2 (prompt libraries) — **Done 2026-05-30**. Both agents already build `ChatClientAgent`s via `ToolCallingAgentFactory` and render system prompts via `IPromptLibrary`.

## Overview

Collapse the two near-duplicate MCP client modules (`InfraGate.Observer/Mcp/*` and `InfraGate.Planner/Mcp/*`) into one shared deep module, **`InfraGate.AgentMcp`**, that owns gateway connection, client-credentials auth, tool listing, and raw tool invocation behind a single small interface. In the same effort, make **OAuth scope the source of truth for which tools an agent loads** by teaching the gateway's `ListTools` to advertise only the tools the caller's scope permits (and to preserve the MCP `ReadOnlyHint` annotation it currently strips). The two hardcoded client-side tool-name whitelists (`ObserverConventions.ToolNames.ReadOnlyToolNames`, `PlannerConventions.ToolNames.{ReadOnlyToolNames,AllowedToolNames}`) and the two `*ToolWhitelist.AssertAllowed` guards then disappear — the agent loads whatever the scope-filtered catalog returns and hands the **read-only-hinted** subset to the LLM.

The two deterministic (non-LLM) call paths stay deterministic: the Observer's **Snapshot** pre-fetch and the Planner's `propose_plan` call continue to run as explicit code, not as LLM tool calls. The LLM never sees `propose_plan`.

## Background — current state (verified)

| | Observer | Planner |
|---|---|---|
| Client | `Mcp/ObserverMcpClient.cs` (`IObserverMcpClient`, DI singleton) | `Mcp/PlannerMcpClient.cs` (`IPlannerMcpClient`, DI singleton) |
| Connect/auth | `ConnectAsync`: `ClientCredentialsBearerHandler` + `HttpClientTransport` + `McpClient.CreateAsync` (inline) | identical (`CreateHttpClient` + same transport) |
| Tools → LLM | `GetReadOnlyToolsAsync` → `ListToolsAsync().Where(name ∈ ReadOnlyToolNames).Cast<AITool>()` → `ObservationCycleRunner.RunAsync:108` → `ToolCallingAgentFactory.Create(tools:)` | `GetReadOnlyToolsAsync` (same filter) → `BatchProcessor.ProcessBatchAsync:66` → `DecideExecutor` via `ToolCallingAgentFactory.Create(tools:)` |
| Deterministic call path | `GetToolResultAsync` (whitelist-guarded) → `SnapshotFetcher.FetchToolSafeAsync` pre-fetches 6 read-only tools | `CallToolAsync` (whitelist-guarded) → `ProposeExecutor.ProposePlanAsync` calls `propose_plan` |
| Client-side whitelist | `Mcp/ToolWhitelist.cs` + `ObserverConventions.ToolNames.ReadOnlyToolNames` (8 names) | `Mcp/PlannerToolWhitelist.cs` + `PlannerConventions.ToolNames.{ReadOnlyToolNames(8), AllowedToolNames(9)}` |
| OAuth scope (token) | `mcp:tools.readonly` | `mcp:tools.propose mcp:tools.readonly` |

**Gateway reality (verified):**
- `GatewayToolDispatcher.ListToolsAsync` returns the **full catalog to every authenticated caller**: all read-only forwarded tools + every destructive tool re-exposed as `request_<name>` + `propose_plan` + `wait_for_plan_approval` + `execute_approved_plan` + `get_plan_status`. **There is no scope filtering on `ListTools`.**
- Scope is enforced **only on `CallTool`**, via `IToolScopeGuard` against `McpGatewayConventions.ToolScopeRequirements` (`MutationScope="mcp:tools"`, `ReadOnlyScope="mcp:tools.readonly"`, `ProposeScope="mcp:tools.propose"`, `ExecuteScope="mcp:tools.execute"`). The per-tool scope map lives inline in `GatewayToolDispatcher.CallToolAsyncCore`.
- `ToolDefinitionFactory.CreateForwardedTool` copies only `Name`/`Description`/`InputSchema` — it **strips** the `ReadOnlyHint`/`DestructiveHint` annotations. The gateway *does* know read-only vs destructive internally (`DownstreamMcpClient` reads `ProtocolTool.Annotations?.ReadOnlyHint`/`DestructiveHint` into `DownstreamTool`), but never advertises it to agents.
- **Consequence:** the client-side hardcoded whitelist is doing real work today — it is the only thing stopping the Observer's LLM from *seeing* `propose_plan`/`request_*`/`execute_approved_plan`. Removing it safely **requires** the gateway to filter `ListTools` by scope first.

**Framework reality (confirmed against the local clone `~/OtherRepos/agent-framework`, and against `Microsoft.Agents.AI` 1.8.0 referenced by both agents):** the Microsoft Agent Framework has **no scope-aware MCP tool-provider abstraction**. MCP tools are plain `AITool`s; the canonical pattern is exactly what InfraGate already does — `McpClient.ListToolsAsync().Cast<AITool>()` → `tools:` (see sample `02-agents/.../Agent_Step09_UsingMcpClientAsTools`). The `Microsoft.Agents.AI.Mcp` package only adds (a) `McpClientTaskExtensions.ListAgentToolsWithTaskSupportAsync` — long-running SEP-2663 task wrapping, irrelevant to our short synchronous read-only calls — and (b) `AgentMcpSkillsSource` — SEP-2640 `SKILL.md` discovery, unrelated. **§1 already removed the "custom tool-calling loop"** (`FunctionInvokingChatClient` handles iteration). So the roadmap's "build an MCP Tool-Provider for the framework / LLM router decides without custom loop logic" maps to: *build our own shared module* (as §2 built `InfraGate.Prompts` instead of adopting Declarative) **+ make the gateway catalog scope-aware**, not adopt a framework type.

## Architecture decisions

- **D1 — Build a shared MCP toolset seam; don't adopt a framework type.** Introduce `InfraGate.AgentMcp` exposing one small interface (`IAgentMcpToolset`) that hides connect + auth + list + call. *Deletion test:* removing it re-scatters the connect/auth/list/call plumbing back into Observer **and** Planner → complexity reappears across two callers → the seam earns its keep. **Two adapters = real seam** (Observer + Planner). The `Microsoft.Agents.AI.Mcp` package is **considered and rejected** (task support + skills discovery, neither needed; would add surface for no leverage).
- **D2 — New project, not `InfraGate.AgentLlm`.** Honors the recorded decision *"InfraGate.AgentLlm — narrow scope is intentional (LLM plumbing only)."* `InfraGate.AgentMcp` mirrors `InfraGate.AgentLlm`/`InfraGate.Prompts`: one public interface + DI extension, everything else `internal`.
- **D3 — Scope is the source of truth; the gateway advertises a scope-filtered catalog.** Extract a single tool→required-scope authority consulted by **both** `ListTools` (to filter the advertised catalog) and `CallTool` (to enforce, as today). A caller with `mcp:tools.readonly` sees only read-only tools; `mcp:tools.propose` additionally sees `propose_plan`; `mcp:tools` (human clients) sees `request_*` + approval-control; `mcp:tools.execute` (Executor) sees `wait_for_plan_approval`/`execute_approved_plan`/`get_plan_status`. *This is a defense-in-depth + correctness improvement, not a weakening:* the `CallTool` scope gate remains the authoritative hard guarantee; the filtered catalog merely stops agents from *seeing* tools they may not call.
- **D4 — Preserve `ReadOnlyHint` on forwarded tools; the client splits by hint, not by a name list.** `CreateForwardedTool` carries the downstream `ReadOnlyHint`; gateway-synthesized tools (`request_*`, `propose_plan`, approval-control) are advertised as **not** read-only. The toolset's "tools for the LLM" = the read-only-hinted subset of the scope-filtered catalog. The two hardcoded name HashSets and both `*ToolWhitelist` classes are deleted.
- **D5 — Deterministic paths stay deterministic.** The Observer **Snapshot** pre-fetch and the Planner `propose_plan` call use `IAgentMcpToolset.CallToolAsync` (raw, outside the LLM loop). The LLM tool list (read-only-hinted) **never** contains `propose_plan`. A Planner test asserts this explicitly — it is security-relevant (a leaked `propose_plan` tool would let the LLM bypass `ValidateExecutor`).
- **D6 — Result interpretation stays with each caller.** `CallToolAsync` returns the raw `CallToolResult`. The Observer keeps its "join `TextContentBlock` text, null on error" logic in `SnapshotFetcher`; the Planner keeps its "serialize + extract `planId`" logic in `ProposeExecutor`. The shared module owns the *machinery* (connect/list/call), not the domain-specific *interpretation*.
- **D7 — No mocks.** Per repo rule, `InfraGate.AgentMcp.Tests` exercises the real toolset against an in-process MCP server fixture (`ModelContextProtocol.AspNetCore` `TestServer`), mirroring the existing `ObserverGatewayIntegrationTests`/`PlannerGatewayIntegrationTests` fixtures. Gateway scope-filter tests reuse the existing `GatewayToolDispatcherTests` harness.

## Target shape (sketch — finalize during implementation)

```csharp
// InfraGate.AgentMcp — the only surface Observer/Planner depend on
public interface IAgentMcpToolset
{
    string GatewayBaseUrl { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);

    // Read-only-hinted tools from the scope-filtered catalog, ready for ToolCallingAgentFactory.Create(tools:).
    Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken);

    // Raw invocation for the deterministic paths (Observer Snapshot pre-fetch, Planner propose_plan).
    Task<CallToolResult> CallToolAsync(
        string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}
```

```csharp
// InfraGate.AgentMcp — McpClient hidden inside; bearer auth via InfraGate.ClientCredentials
internal sealed class GatewayAgentMcpToolset(
    AgentMcpOptions options, IClientCredentialsTokenProvider tokenProvider, ILoggerFactory loggerFactory)
    : IAgentMcpToolset, IAsyncDisposable
{
    public async Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken ct)
    {
        var tools = await Client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        // Gateway already scope-filtered the catalog; hand the LLM only the read-only-hinted tools.
        return tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).Cast<AITool>().ToList();
    }
    // ConnectAsync / CallToolAsync / DisposeAsync … (the plumbing both clients duplicate today)
}
```

```csharp
// InfraGate.McpGateway — one authority both ListTools and CallTool consult (no drift)
internal static class ToolScopeCatalog
{
    // returns the scopes that authorize a given tool name (mirrors CallToolAsyncCore today)
    public static IReadOnlyList<string> RequiredScopesFor(string toolName) => …;
}
```

---

## Task list

### Phase 1: Foundation — the `InfraGate.AgentMcp` module

#### Task 1: Scaffold `InfraGate.AgentMcp` project + `IAgentMcpToolset` seam
**Description:** Create the shared library with the public `IAgentMcpToolset` interface, the internal `GatewayAgentMcpToolset` (connect via `ClientCredentialsBearerHandler` + `HttpClientTransport` + `McpClient.CreateAsync`; `GetAgentToolsAsync` filters by `ReadOnlyHint`; `CallToolAsync` returns raw `CallToolResult`), an `AgentMcpOptions` record (gateway base URL + transport name), and `AddInfraGateAgentMcp(...)` DI extension. Lift the duplicated connect/auth plumbing from both existing clients.
**Acceptance criteria:**
- [ ] `GetAgentToolsAsync` returns only `ReadOnlyHint == true` tools as `AITool`; `CallToolAsync` returns the raw `CallToolResult` (no client-side name whitelist anywhere).
- [ ] Surface is `internal` except `IAgentMcpToolset`, `AgentMcpOptions`, and the DI extension. `sealed`, file-scoped namespace, primary ctors, `ConfigureAwait(false)`, `CancellationToken` threaded.
- [ ] References `Microsoft.Extensions.AI` (10.6.0), `ModelContextProtocol` (1.3.0), `InfraGate.ClientCredentials`; `InternalsVisibleTo InfraGate.AgentMcp.Tests`.
**Verification:** `dotnet build src/InfraGate.AgentMcp/InfraGate.AgentMcp.csproj`; added to `InfraGate.slnx`.
**Dependencies:** None
**Files likely touched:** `src/InfraGate.AgentMcp/{InfraGate.AgentMcp.csproj,IAgentMcpToolset.cs,GatewayAgentMcpToolset.cs,AgentMcpOptions.cs,AgentMcpServiceCollectionExtensions.cs,GlobalUsings.cs}`, `InfraGate.slnx`
**Estimated scope:** Medium

#### Task 2: `InfraGate.AgentMcp.Tests` against an in-process MCP server
**Description:** Add the test project with an in-process MCP server fixture (`ModelContextProtocol.AspNetCore` `TestServer`) exposing a mix of read-only-hinted and non-read-only tools. Test the real toolset (no mocks).
**Acceptance criteria:**
- [ ] `GetAgentToolsAsync_ReturnsOnlyReadOnlyHintedTools`, `GetAgentToolsAsync_ExcludesNonReadOnlyTools`, `CallToolAsync_ReturnsRawResult`, `ConnectAsync_Idempotent`.
- [ ] No Moq/NSubstitute; deterministic; no live cluster/Keycloak.
**Verification:** `dotnet test tests/InfraGate.AgentMcp.Tests/InfraGate.AgentMcp.Tests.csproj`
**Dependencies:** Task 1
**Files likely touched:** `tests/InfraGate.AgentMcp.Tests/{InfraGate.AgentMcp.Tests.csproj,GlobalUsings.cs,IntegrationTests/GatewayAgentMcpToolsetTests.cs,IntegrationTests/InProcessMcpServerFixture.cs}`
**Estimated scope:** Medium

### Checkpoint: Foundation
- [ ] `InfraGate.AgentMcp` builds; tests pass; full solution still builds.

---

### Phase 2: Gateway — scope-aware tool catalog

#### Task 3: Single tool→scope authority + preserve `ReadOnlyHint`
**Description:** Extract the per-tool scope requirements currently inline in `GatewayToolDispatcher.CallToolAsyncCore` into one authority (`ToolScopeCatalog.RequiredScopesFor`) and route `CallTool` enforcement through it (behavior-preserving). Update `ToolDefinitionFactory.CreateForwardedTool` to preserve the downstream `ReadOnlyHint` annotation; advertise gateway-synthesized tools (`request_*`, `propose_plan`, approval-control) as **not** read-only.
**Acceptance criteria:**
- [ ] `CallTool` scope decisions are byte-identical to today (the authority reproduces the existing map: read-only⇒any-tool-scope; `request_*`⇒mutation; `propose_plan`⇒mutation|propose; `get_plan_status`⇒mutation; `wait`/`apply`⇒mutation|execute).
- [ ] Forwarded read-only tools carry `Annotations.ReadOnlyHint = true`; synthesized tools do not.
- [ ] Existing `GatewayToolDispatcherTests` / `GuardedToolRunnerTests` still green (no behavior change at `CallTool`).
**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
**Dependencies:** None (parallelizable with Phase 1)
**Files likely touched:** `src/InfraGate.McpGateway/McpTransport/Dispatch/{ToolScopeCatalog.cs(new),ToolScopeGuard.cs,ToolDefinitionFactory.cs}`, `src/InfraGate.McpGateway/McpTransport/GatewayToolDispatcher.cs`
**Estimated scope:** Medium

#### Task 4: Filter `ListTools` by caller scope
**Description:** Make `GatewayToolDispatcher.ListToolsAsync` consult `ToolScopeCatalog` + the caller's scopes (`httpContextAccessor.HttpContext.User`, via `GatewayAuthentication.HasRequiredScope`) and advertise only tools the caller may call. Keep building the same tool definitions; filter the final list.
**Acceptance criteria:**
- [ ] `mcp:tools.readonly` token ⇒ only read-only tools (all carry `ReadOnlyHint`); `mcp:tools.propose`(+readonly) ⇒ read-only + `propose_plan`; `mcp:tools`(mutation) ⇒ read-only + `request_*` + approval-control; `mcp:tools.execute` ⇒ `wait`/`apply`/`get_plan_status` (+ read-only if granted).
- [ ] New `GatewayToolDispatcherTests` cases per scope; updated `GatewayHttpMcpIntegrationTests` / Safety-E2E assertions that previously assumed the full catalog.
- [ ] No regression for human clients (mutation scope) or the Executor (execute scope).
**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/...`; opt-in `dotnet test tests/InfraGate.Safety.E2E.Tests/...` if catalog assertions changed.
**Dependencies:** Task 3
**Files likely touched:** `src/InfraGate.McpGateway/McpTransport/GatewayToolDispatcher.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayToolDispatcherTests.cs`, `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs` (+ Safety-E2E if needed)
**Estimated scope:** Medium

### Checkpoint: Gateway
- [ ] Gateway builds; scope-filtered `ListTools` proven per scope; `CallTool` behavior unchanged; `ReadOnlyHint` forwarded.

---

### Phase 3: Observer migration

#### Task 5: Swap Observer to `IAgentMcpToolset`; delete the Observer client + whitelist
**Description:** Replace `IObserverMcpClient`/`ObserverMcpClient`/`ToolWhitelist` with `IAgentMcpToolset`. `ObservationCycleRunner.RunAsync` calls `GetAgentToolsAsync`; `SnapshotFetcher.FetchToolSafeAsync` calls `CallToolAsync` and keeps its local text-join + null-on-error handling (move `IsError`/text extraction into the fetcher). Delete `ObserverConventions.ToolNames.ReadOnlyToolNames` (keep the individual tool-name constants the `SnapshotFetcher` still uses). Wire DI (`AddInfraGateAgentMcp`) + the `ConnectObserverMcpClientAsync` startup call; add the `ProjectReference`.
**Acceptance criteria:**
- [ ] `ObservationCycleRunner`/`SnapshotFetcher` depend only on `IAgentMcpToolset`; `ObserverMcpClient`, `IObserverMcpClient`, `ToolWhitelist`, and the `ReadOnlyToolNames` HashSet are gone.
- [ ] Observer unit + integration tests updated to the new seam; all pass (incl. `RunAsync_ToolCallsIncrementCounter`, snapshot-fetch-throws path).
**Verification:** `dotnet test tests/InfraGate.Observer.Tests/...` and `tests/InfraGate.Observer.IntegrationTests/...`
**Dependencies:** Tasks 2, 4
**Files likely touched:** `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`, `src/InfraGate.Observer/Snapshot/SnapshotFetcher.cs`, `src/InfraGate.Observer/ObserverConventions.cs`, `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Observer/InfraGate.Observer.csproj`; delete `src/InfraGate.Observer/Mcp/{ObserverMcpClient.cs,IObserverMcpClient.cs,ToolWhitelist.cs}`; Observer test files.
**Estimated scope:** Large

### Checkpoint: Observer
- [ ] Observer builds and all its tests pass; the Observer's LLM toolset is the scope-filtered read-only catalog (no hardcoded names); deletions leave no dangling refs (build proves it).

---

### Phase 4: Planner migration

#### Task 6: Swap Planner to `IAgentMcpToolset`; delete the Planner client + whitelist
**Description:** Replace `IPlannerMcpClient`/`PlannerMcpClient`/`PlannerToolWhitelist` with `IAgentMcpToolset`. `BatchProcessor.ProcessBatchAsync` calls `GetAgentToolsAsync` (read-only only); `ProposeExecutor.ProposePlanAsync` calls `CallToolAsync("propose_plan", …)` and keeps its local `planId` extraction (serialize the returned `CallToolResult` first to preserve `TryExtractPlanId` behavior). Delete `PlannerConventions.ToolNames.{ReadOnlyToolNames,AllowedToolNames}` (keep `ProposePlan` + arg constants). Wire DI + startup connect; add the `ProjectReference`.
**Acceptance criteria:**
- [ ] `BatchProcessor`/`ProposeExecutor` depend only on `IAgentMcpToolset`; `PlannerMcpClient`, `IPlannerMcpClient`, `PlannerToolWhitelist`, and both HashSets are gone.
- [ ] **`GetAgentToolsAsync` for the Planner excludes `propose_plan`** — explicit test asserts the LLM toolset contains no mutation/propose tool (security guarantee).
- [ ] Planner unit + integration tests updated; `DecideExecutor`/`ValidateExecutor` tests untouched; all pass.
**Verification:** `dotnet test tests/InfraGate.Planner.Tests/...` and `tests/InfraGate.Planner.IntegrationTests/...`
**Dependencies:** Tasks 2, 4
**Files likely touched:** `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/Cycle/Workflow/ProposeExecutor.cs`, `src/InfraGate.Planner/PlannerConventions.cs`, `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/InfraGate.Planner.csproj`; delete `src/InfraGate.Planner/Mcp/{PlannerMcpClient.cs,IPlannerMcpClient.cs,PlannerToolWhitelist.cs}`; Planner test files.
**Estimated scope:** Large

### Checkpoint: Planner
- [ ] Planner builds and all its tests pass; both agents share one MCP seam; both client modules + both whitelists are deleted; `propose_plan` stays deterministic and invisible to the LLM.

---

### Phase 5: Docs, glossary, roadmap

#### Task 7: Module README + glossary + roadmap correction + ADR
**Description:** Add `src/InfraGate.AgentMcp/README.md` (purpose, `IAgentMcpToolset`, scope-filtered loading, deterministic-call usage). Add the new module to the `AGENTS.md` Solution Map and the `repo-onboarding` README table. Add a glossary term to `CONTEXT.md` (verify absent first) — e.g. **Agent MCP Toolset**: *the shared seam through which the Anomaly Observer and Remediation Planner load scope-filtered gateway tools and invoke deterministic tool calls.* Update the gateway README (scope-filtered `ListTools` + forwarded `ReadOnlyHint`) and the Observer/Planner READMEs (no more client-side whitelist). Correct roadmap §3 wording (§1 already removed the loop; framework has no scope-aware provider; scope-filtered catalog is the mechanism). Offer an ADR recording D3+D4 (scope-filtered catalog + shared toolset) so future reviews don't re-litigate.
**Acceptance criteria:**
- [ ] New README accurate to code; glossary term added; roadmap §3 no longer implies a non-existent framework capability or a still-present custom loop.
**Verification:** Manual read-through; links resolve.
**Dependencies:** Tasks 5, 6
**Files likely touched:** `src/InfraGate.AgentMcp/README.md`, `AGENTS.md`, `.agents/skills/repo-onboarding/SKILL.md`, `CONTEXT.md`, `src/InfraGate.McpGateway/README.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md`, `docs/adr/0024-*.md`
**Estimated scope:** Medium

### Checkpoint: Complete
- [ ] `dotnet build` + `dotnet test` (AgentMcp, McpGateway, Observer, Planner) all green.
- [ ] One `IAgentMcpToolset` seam; two client modules + two whitelists deleted; scope drives the catalog; `ReadOnlyHint` forwarded; `propose_plan` deterministic.
- [ ] ADR offered/recorded (D3+D4). Ready for review.

---

## Parallelization

- **Independent foundations:** Phase 1 (`InfraGate.AgentMcp`, Tasks 1→2) and Phase 2 (gateway, Tasks 3→4) can proceed in parallel — they share no files. Gateway Task 4 is the high-risk task (touches a contract many tests assume); schedule it early.
- **Converge:** Phase 3 (Observer) and Phase 4 (Planner) each need Task 2 **and** Task 4 (the scope-filtered catalog must exist before the client whitelist is removed). Observer and Planner migrations are then independent of each other.
- **Docs (Task 7) last.**

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Removing the client whitelist before the gateway filters `ListTools` would expose mutation/approval tools to the Observer's LLM | **High** | Hard-order: Task 4 (scope-filtered catalog) is a dependency of Tasks 5 & 6. The `CallTool` scope gate remains authoritative regardless. |
| Scope-filtering `ListTools` breaks tests/clients that assume the full catalog | Med | Mirror the existing `CallTool` scope map exactly via one `ToolScopeCatalog`; add per-scope `ListTools` tests; update `GatewayHttpMcpIntegrationTests`/Safety-E2E assertions; verify human (mutation) + Executor (execute) catalogs explicitly. |
| Planner LLM could see `propose_plan` if the read-only-hint split is wrong | **High** | Gateway advertises `propose_plan` as **not** read-only (Task 3); `GetAgentToolsAsync` returns only `ReadOnlyHint==true`; a Planner test asserts the LLM toolset excludes `propose_plan` (Task 6). |
| `CallToolResult` interpretation differs between callers (Observer text vs Planner JSON) | Low | `CallToolAsync` returns the raw result; each caller keeps its own extraction (D6) — preserves `SnapshotFetcher` null-on-error and `ProposeExecutor.TryExtractPlanId` behavior verbatim. |
| Downstream tools lack `ReadOnlyHint` ⇒ agent toolset comes back empty | Med | Verify the downstream McpServer sets `ReadOnlyHint` on its read-only tools (it already feeds `DownstreamTool.IsReadOnly`); add a gateway test that forwarded read-only tools carry the hint; fail loudly (log) if an agent's toolset is empty. |
| Re-introducing a shared module adds a project | Low | Symmetric with `InfraGate.AgentLlm`/`InfraGate.Prompts`; net deletion of two client modules + two whitelists. |

## Open questions

- **Interface name/shape.** `IAgentMcpToolset` vs `IAgentGatewaySession`; whether `GetAgentToolsAsync` should expose a "raw catalog" overload for future non-read-only agents. Recommend finalizing in Task 1; keep the surface minimal.
- **Should the client keep a thin defense-in-depth assertion?** The user chose "scope-aware catalog" (whitelist disappears). The gateway `CallTool` gate is authoritative, so no client assertion is required; revisit only if a future agent needs a stricter local guard.
- **ADR number.** `docs/adr/0023` is the SK-renderer ADR (§2); this plan assumes `0024` — confirm the next free number at Task 7.
- **Scope-filtered `ListTools` and MCP `list_changed`.** Out of scope here; the catalog is static per-token. Note for future work if tools become dynamic.
