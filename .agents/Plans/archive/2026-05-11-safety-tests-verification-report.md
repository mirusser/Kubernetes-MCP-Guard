# Plan Verification: `safety-tests-implementation-plan.md`

Verified against:
- `.agents/skills/repo-onboarding/SKILL.md`
- `.agents/skills/code-standards/SKILL.md`
- `.agents/skills/writing-tests/SKILL.md`
- Parent plan `.agents/Plans/minimum-for-demo.md` §6

---

## 1. Current State vs. Plan

The project is partially scaffolded. Already done:
- `InfraGate.Safety.E2E.Tests.csproj` — matches the plan (minor diff: `ModelContextProtocol` 1.2.0 instead of `ModelContextProtocol.Client`)
- `GlobalUsings.cs` — exists but only has `Xunit` and `InfraGate.Approvals` (thin; might need more for tests)
- `README.md` — written, good
- `InfraGate.slnx` — project entry already added (Task 2 is done)

**Not yet done**: `SafetyE2EFixture.cs`, `SafetyE2ECollection.cs`, and all 7 `Workflows/*.cs` files.

---

## 2. Conformance to `writing-tests/SKILL.md`

| Convention | Plan check | Issue? |
|---|---|---|
| Test project under `tests/` with matching naming | `tests/InfraGate.Safety.E2E.Tests/` — follows pattern | OK |
| Split: `UnitTests/` vs `IntegrationTests/` | Plan uses `Workflows/` instead of `IntegrationTests/` | **Minor deviation** — `Workflows/` is a new convention not documented in the writing-tests skill. The existing `McpServerIntegrationTests` lives directly under `IntegrationTests/`. This isn't wrong per se, but it creates a third directory convention. |
| Naming: `Method_State_ExpectedResult` | Plan explicitly states this on line 90 | OK |
| `InternalsVisibleTo` setup | Plan doesn't mention checking it. After investigation: **no entries needed** — all types the fixture/test constructs are `public` (`McpGatewayOptions`, `GatewayAuthOptions`, `GuardedToolRunner`, `PromptInjectionGuard`, `IDownstreamMcpClient`, `K8sGatewayTools`, `K8SMcpOptions.DefaultNamespace`, etc.). | No issue |
| One test class per production class | Plan says "One class per file... one per demo bullet". Since these are E2E workflow tests, there's no single production class to map to. Acceptable for E2E. | OK |
| `[Theory]` over duplicated `[Fact]` | Plan uses `[Fact]` for most. Some (Task 13, DryRunFailureTests at request vs apply time) could be `[Theory]` with `[InlineData]` but separate `[Fact]`s are also fine. | Minor suggestion |
| No shared mutable state | Plan creates per-test approval root subdirectories via `Guid.NewGuid()`. | OK |
| Assert on observable outputs | Plan asserts refusal text, audit events, file presence. | OK |

**Writing-tests gaps:**

- **No `[Trait("Category", "SafetyE2E")]` in place**: The plan mentions it on line 42, and the README mentions the filter `--filter "Category=SafetyE2E"`, but neither the `GlobalUsings.cs` nor any existing code applies it yet. This will be done in Phase 3 — specs are correct.

- **Default test filter in the plan**: Line 134 says `"Category!=Keycloak&Category!=SafetyE2E"` which correctly excludes both opt-in categories.

---

## 3. Conformance to `code-standards/SKILL.md`

| Convention | Plan check | Issue? |
|---|---|---|
| Magic strings | Plan explicitly says "no string literals" for audit events, references `ApprovalConventions.AuditEvents` (line 161). | OK — plan says to use constants |
| `sealed` classes | Plan says `sealed class XyzTests` (line 90) | OK |
| File-scoped namespaces | Plan specifies `namespace InfraGate.Safety.E2E.Tests.Workflows;` | OK |
| One type per file | Plan says one class per file, 7 files | OK |
| `ConfigureAwait(false)` | Not mentioned. In test code this is less critical but library code called through tests already has it. | Minor — should perhaps be mentioned for helper methods in the fixture |
| Lower camel case, no `_` prefix, no `var` for primitives | Plan doesn't detail field-level naming — acceptable at plan level | OK |
| Analyzer hygiene: `#pragma warning disable` | `KeycloakIntegrationTests.cs` uses `#pragma warning disable ASPDEPR004` and `ASPDEPR008`. The plan doesn't mention needing these. Since the fixture mirrors `CreateGatewayServer`, it may need them too. | **Missing** — the plan should note that the fixture will need these pragmas |
| `[LoggerMessage]` source gen, structured logging | Not applicable to tests | OK |

**code-standards issues found:**

1. **Missing `#pragma warning disable`**: The `KeycloakIntegrationTests` uses `ASPDEPR004`/`ASPDEPR008` pragmas. The plan's fixture mirrors `CreateGatewayServer` which uses deprecated ASP.NET patterns (`TestServer`, `WebHostBuilder`). The plan should explicitly note this or risk compilation warnings.

2. **`ModelContextProtocol` vs `ModelContextProtocol.Client`**: The plan specifies `ModelContextProtocol.Client` but the existing csproj uses `ModelContextProtocol` 1.2.0. The plan's reference list should be updated to match the actual csproj.

3. **`GlobalUsings.cs` is thin**: The plan says it should have "Xunit, InfraGate.Approvals, common namespaces". The existing file only has `Xunit` and `InfraGate.Approvals`. The fixture will need additional namespaces (e.g., `InfraGate.McpGateway`, `Microsoft.AspNetCore.TestHost`, `Testcontainers.Keycloak`). The plan is vague on which "common namespaces" but that's fine for plan-level.

---

## 4. Conformance to `repo-onboarding/SKILL.md`

| Convention | Plan check | Issue? |
|---|---|---|
| References correct READMEs/docs | Plan references `KeycloakIntegrationTests`, `McpServerIntegrationTests`, `GatewayApprovalService`, `ApprovalConventions`, `InfraGate.slnx`, etc. — all correct paths | OK |
| Does not create "just in case" abstractions | Plan explicitly says "No copied helpers" and references existing patterns | OK |
| Surgical changes | Plan says "No existing source or test files are otherwise modified" — good | OK |

**repo-onboarding issues found:**

1. **`AGENTS.md` test project list is stale**: The current AGENTS.md (line 109-110) lists only `InfraGate.McpServer.Tests`, `InfraGate.McpGateway.Tests`, and `InfraGate.DevIssuer.Tests` — it's **missing** `InfraGate.McpGateway.KeycloakTests`. The plan's Task 14 says to add `Safety.E2E.Tests` but doesn't notice this pre-existing gap. Two entries need adding, not one.

2. **Plan references `/agents/Plans/minimum-for-demo.md`**: This is allowed per the skill (line 30: "Read it only when the user explicitly asks for plans, roadmap details, or historical context") since the user explicitly asked about it.

---

## 5. Factual Accuracy of Plan References

| Plan reference | Verified? | Notes |
|---|---|---|
| `ApprovalConventions.AuditEvents` at `:L22-L38` | Line 22-38 — correct | `PlanRequested`, `ApprovalHashMismatch`, `PlanApplied`, `ApplyDenied`, `DryRunFailed`, `ApprovalChallengeExpired`, etc. — all constants exist |
| `KeycloakIntegrationTests:41` for Keycloak container pattern | Line 42 — correct | `await keycloakContainer.StartAsync()` |
| `McpServerIntegrationTests:36-50` for subprocess spawn | Lines 35-49 — correct | Uses `StdioClientTransport` with env vars |
| ApprovalChallengeStore `ExpiresAtUtc` manipulation | `ApprovalChallenge` is a `record` with positional properties — supports `with` expressions. `SaveAsync` exists. | Feasible — read challenge, `with { ExpiresAtUtc = past }`, save back |
| `GatewayApprovalService` "same authenticated subject" check | Line 264 returns `"Approval requires the same authenticated subject that requested the plan."` — also line 195 for deny path | Correct — the plan's expected text matches |
| `deploy/keycloak/infra-gate-realm.json` has only `demo` user | Confirmed — only `demo` user | Task 12 will need either adding a second user or using a different approach |
| `InfraGate.slnx` format is XML with `<Project Path="..." />` | Confirmed — `<Project Path="tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj" />` | Already present |

---

## 6. Task-Level Issues

**Task 5 (Fixture):** The plan says the fixture exposes `CallToolAsync(client, toolName, args)` wrapping the gateway MCP HTTP endpoint. But the `KeycloakIntegrationTests` fixture uses `NullDownstreamClient` (returns `"{}"`). The SafetyE2E fixture needs a **real downstream** (the spawned McpServer subprocess) via an `McpClient`. The plan mentions this on line 59: "spawn dotnet run --project src/InfraGate.McpServer" — but the gateway uses `IDownstreamMcpClient` to communicate with the server. The fixture must either:
- Make the gateway talk to the subprocess TCP endpoint (if the server has one), OR
- Replace the `IDownstreamMcpClient` with a real one wrapping the subprocess's `McpClient` transport

The plan is **vague on this wiring detail**.

**Task 12 (WrongUserApproval):** The plan says "hit the approval HTTP endpoint as B". The approval endpoint is `MapGatewayApprovalEndpoints()` which serves browser-based approval (`/approval/{challengeId}`). The test needs to:
1. Acquire the challenge ID from the request response
2. Auth as user B
3. POST to the approve endpoint

The plan is correct that the realm JSON needs a second user. The `KeycloakIntegrationTests` uses password grant for `demo`. The plan mentions possibly modifying `infra-gate-realm.json` — it should make a clear decision rather than deferring.

**Task 13A (DryRunFailure at request):** The plan says "manifest containing a field that fails admission/server-side validation (e.g. invalid replica count)". Kubernetes' default validation doesn't reject negative replica counts at dry-run. A better approach is to use `fieldValidation=Strict` with an unknown field, or use a resource with an invalid spec field that the API server rejects. The plan's example may not trigger a real dry-run failure.

---

## 7. Missing Pieces

1. **No `ApprovalStore` reference in project references for parsing audit**: The fixture reads audit events from `audit.jsonl`. `ApprovalStore.WriteAuditAsync` writes audit events. The plan says the fixture has `ReadAuditEventsAsync()` — but there's no explicit `ReadAuditEvents` method on `ApprovalStore`. The fixture will need to parse `audit.jsonl` directly from the filesystem, or the plan should add this detail.

2. **No mention of the MCP subprocess's gateway communication**: The gateway needs an `IDownstreamMcpClient` to call tools. The `KeycloakIntegrationTests` uses `NullDownstreamClient`. For E2E, the fixture needs to wrap the subprocess's `McpClient` in an `IDownstreamMcpClient` adapter. This is a non-trivial wiring gap.

3. **Gateway MCP HTTP endpoint**: The plan says `CallToolAsync` wraps the "gateway MCP HTTP endpoint". The gateway exposes MCP over HTTP at `McpGatewayConventions.McpPath` (`/mcp` by default). The test must send MCP JSON-RPC over HTTP, not direct tool calls. The `ModelContextProtocol` client can connect via HTTP transport. The fixture must create an `McpClient` that talks to the gateway's HTTP endpoint (not the subprocess directly).

4. **Task 8 (`ExpiredApprovalTests`) overrides `ExpiresAtUtc`**: The plan says "manipulate the stored challenge to expire it (write `ExpiresAtUtc` in the past via `ApprovalChallengeStore`)". This is feasible using `challengeStore.GetAsync()` → `with { ExpiresAtUtc = ... }` → `challengeStore.SaveAsync()`. The plan should clarify that `SaveAsync` overwrites the challenge file.

5. **Task 11 (`ModifiedPendingPlanTests`) verifies "changed after approval"**: The actual production message is `"The pending plan changed after this approval URL was created."` (line 281-282). The plan says "changed after approval" — the test assertion should match the actual text.

---

## 8. Summary

| Area | Verdict |
|---|---|
| Scope completeness | Covers all 7 bullets from `minimum-for-demo.md` §6. Each gets its own file. |
| Project structure | Follows conventions, already partially scaffolded. |
| Code conventions | Follows `sealed`, file-scoped namespaces, one-type-per-file, `Method_State_ExpectedResult`. |
| Audit assertion hygiene | References `ApprovalConventions.AuditEvents` constants — good. |
| Existing pattern fidelity | Mirrors `KeycloakIntegrationTests` and `McpServerIntegrationTests` patterns closely. |
| Task list granularity | Good phased breakdown with verification checkpoints. |

**Issues found (by severity):**

| # | Severity | Issue |
|---|---|---|
| 1 | High | Fixture wiring gap: the plan is vague about how the gateway's `IDownstreamMcpClient` connects to the spawned McpServer subprocess. The `NullDownstreamClient` stub from `KeycloakIntegrationTests` will not exercise production safety code. The fixture needs a real `IDownstreamMcpClient` implementation wrapping the subprocess MCP client. |
| 2 | High | `AGENTS.md` test project list is stale — missing `KeycloakTests` and needs `SafetyE2E.Tests`. Plan only mentions adding the latter. |
| 3 | Medium | `#pragma warning disable ASPDEPR004` / `ASPDEPR008` needed in fixture — not mentioned in plan. |
| 4 | Medium | Task 13A (DryRunFailure at request) — "invalid replica count" may not trigger a K8s API server dry-run failure. Consider a manifest with `fieldValidation=Strict` and an unknown field, or a truly invalid spec the API rejects. |
| 5 | Low | `Workflows/` directory is a new convention not in `writing-tests` skill. Should this be `IntegrationTests/Workflows/` or just `IntegrationTests/`? |
| 6 | Low | `ReadAuditEventsAsync()` — no such method exists on `ApprovalStore`. Fixture must parse `audit.jsonl` directly. Plan should clarify the implementation. |
| 7 | Low | Realm JSON needs second user for Task 12; plan defers decision instead of committing to a clear approach. |
| 8 | Low | Plan text `"changed after approval"` doesn't match actual code text `"changed after this approval URL was created"`. |
