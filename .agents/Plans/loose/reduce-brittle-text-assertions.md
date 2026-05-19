# Plan: Reduce Brittle Text Assertions

## Summary

The brittle-test problem is a symptom of shallow behavior interfaces: several modules expose important outcomes only as prose in `Message` fields, so tests and sometimes production code infer behavior by matching user-facing text.

The broad fix is to keep human-readable text as rendering output, and move behavior contracts to typed statuses, stable reason codes, structured state, semantic HTML attributes, and test helpers. This plan intentionally keeps the first implementation pass compatible with existing MCP text responses.

## Architecture Decisions

- Do not change MCP wire shape in the first pass. Existing text responses remain for users and clients while tests move to structural contracts.
- Keep the Generic Approval Core generic. It may expose outcome status and opaque `ReasonCode` strings, but Kubernetes-specific reasons stay in the Kubernetes Adapter.
- Treat reason codes as stable machine contracts, not copied snippets of display text. Codes should represent existing decision branches only.
- Put UI copy behind renderer tests. Behavior tests should assert state, reason code, audit payloads, JSON shape, or domain objects.
- Use semantic HTML attributes as the renderer contract for approval/review pages where the test needs to locate fields or actions.
- Update documentation and the `writing-tests` skill in the same workstream, so the rule is teachable and repeatable.

## Deepening Opportunities

1. Approval result interface
   - Current shallow shape: `bool` plus `Message`.
   - Deeper shape: typed status, stable reason code, optional approval metadata, and preserved human message.
   - Primary target: `ApprovalGateResult`, `ApprovalDecisionResult`, `PreExecutionGateResult`, `PendingPlanResult`, `GrantedPlanResult`, `PlanBuildResult`, `DomainPlanExecutionResult`, and `KubernetesPlanDecodeResult`.

2. Gateway dispatch boundary
   - Current smell: production logic in `GatewayToolDispatcher` branches on formatted text such as `Refused:`.
   - Deeper shape: dispatch branches on `ApprovalGateStatus` and/or `ReasonCode`.
   - Expected effect: tests no longer need to preserve exact prose to preserve behavior.

3. Renderer and review-page contract
   - Current shallow shape: tests locate headings, button labels, and copy.
   - Deeper shape: render stable `data-section`, `data-field`, and `data-action` attributes, while leaving visible copy free to change.
   - Scope: approval pages in the gateway and Kubernetes review rendering.

4. Test guidance and docs
   - Current gap: `.agents/skills/writing-tests/SKILL.md` explains project structure but does not define assertion surfaces.
   - Deeper shape: the skill names which strings are contracts, which strings are presentation, and which helpers to use.
   - Docs should mention any new helper conventions where a README already describes tests that will change.

## Task Breakdown

### Phase 0: Baseline And Scope

Task 0.1: Create a string-assertion inventory.

- Action: run `rg` over `tests/` for direct string assertions, `GetProperty(...)`, `TryGetProperty(...)`, and parsing based on user-facing labels.
- Acceptance criteria:
  - Produce a categorized list of assertion families: protocol/JSON contract, user-facing prose, renderer markup, audit/event contract, env/config keys, and domain constants.
  - Identify the first migration slice by risk, starting with approval-flow and gateway behavior tests.
- Verification:
  - `rg -n 'Assert\.(Equal|Contains|StartsWith|EndsWith|Matches|DoesNotContain)\("[^"]+' tests`
  - `rg -n '(GetProperty|TryGetProperty)\("[^"]+"' tests`
  - `rg -n 'Approval URL:|Approval required\.|Refused:|was approved|same authenticated subject|pending plan changed|missing recorded evidence data' tests src`

Task 0.2: Confirm affected ownership boundaries.

- Action: inspect `CONTEXT.md`, ADRs, and relevant README files before code changes.
- Acceptance criteria:
  - Generic Approval Core changes do not introduce Kubernetes-specific names or policies.
  - Kubernetes Adapter reason codes remain in the adapter convention module.
  - Gateway-specific reasons remain in gateway conventions.
- Verification:
  - Check against `CONTEXT.md`.
  - Check ADRs `0001`, `0002`, `0003`, and `0006` for approval-core/domain-adapter separation.

### Phase 1: Deepen Generic Approval Results

Task 1.1: Add typed approval gate status.

- Action: add `ApprovalGateStatus` with `Approved`, `ApprovalRequired`, and `Refused`.
- Action: extend `ApprovalGateResult` with `Status`, `ReasonCode`, `ApprovalUrl`, `ChallengeId`, and `ExpiresAtUtc` while preserving `Message`.
- Acceptance criteria:
  - Existing call sites still have a human-readable `Message`.
  - New code can distinguish approval-required from refusal without parsing text.
  - The API names do not imply Kubernetes-specific behavior.
- Verification:
  - Focused compile/test for gateway and approval-related tests.

Task 1.2: Add reason codes to generic approval and plan result records.

- Action: add `ReasonCode` to `ApprovalDecisionResult`, `PlanBuildResult`, `DomainPlanExecutionResult`, `PreExecutionGateResult`, `PendingPlanResult`, and `GrantedPlanResult`.
- Action: provide factory overloads or defaults that minimize churn at call sites.
- Acceptance criteria:
  - Failed results in the Generic Approval Core carry a reason code.
  - Successful results may use a success code or no code consistently, documented in the implementation.
  - Message text is not removed or intentionally rewritten.
- Verification:
  - `rg -n 'new (ApprovalDecisionResult|PlanBuildResult|DomainPlanExecutionResult|PreExecutionGateResult|PendingPlanResult|GrantedPlanResult)' src tests`
  - Focused approval-core and gateway tests.

Task 1.3: Replace text-based production branching.

- Action: update `GatewayToolDispatcher` to branch on `ApprovalGateStatus` instead of `gate.Message.Contains("Refused:")`.
- Acceptance criteria:
  - No production code branches on formatted approval/refusal prose.
  - Existing MCP text output is preserved unless a test proves it was already incorrect.
- Verification:
  - `rg -n 'Message\.Contains\("Refused:|Contains\("Refused:' src`
  - Gateway behavior tests.

### Phase 2: Add Domain Adapter Reason Codes

Task 2.1: Add convention-owned reason code constants.

- Action: add reason-code constants in the owning convention modules.
- Generic Approval Core examples:
  - invalid plan id
  - plan not pending
  - plan already applied
  - plan not approved
  - invalid grant
  - missing review evidence
  - challenge not found
  - challenge expired
  - challenge already terminal
  - digest changed
  - pending plan changed
  - requester changed
- Gateway examples:
  - authenticated subject required
  - same subject required
  - approval required
  - adapter decode failed
  - plan not started
  - plan expired
- Kubernetes Adapter examples:
  - unsupported mutation tool
  - missing arguments
  - dry-run failed
  - policy blocked
  - diff evidence failed
  - empty diff
  - live drift
  - pre-execute dry-run failed
  - unsupported adapter
  - unsupported operation
- Acceptance criteria:
  - Constants live with the module that owns the decision.
  - Generic code does not depend on Kubernetes constants.
  - No speculative reason codes are added for branches that do not exist.
- Verification:
  - `rg -n 'ResultReasons|ReasonCode' src tests`
  - Review imports/usings for cross-boundary leakage.

Task 2.2: Populate reason codes in decision branches.

- Action: update `GatewayApprovalService`, `ApprovalStore`, `ApprovalPreExecutionGate`, `KubernetesPlanBuilder`, `KubernetesPlanExecutor`, and Kubernetes decode paths.
- Acceptance criteria:
  - Every failed result factory in the touched approval and Kubernetes paths sets a reason code.
  - Existing messages remain available for logging, rendering, and MCP text responses.
  - Adapter decode failures expose a stable code without making gateway logic understand adapter payload internals.
- Verification:
  - Focused tests for approval store, pre-execution gate, gateway approval service, Kubernetes plan build/decode, and execution.

### Phase 3: Stabilize Test Assertion Surfaces

Task 3.1: Add shared test helpers inside affected test projects.

- Action: add helpers for approval result assertions, reason-code assertions, MCP approval metadata extraction, semantic HTML lookup, and audit JSONL parsing where each test project needs them.
- Acceptance criteria:
  - Helpers are local to test projects unless a real shared test utility already exists.
  - MCP parsing is format-based, for example URL/path/id patterns, not label text such as `Approval URL:`.
  - Audit assertions parse JSON and check event names/payload fields structurally.
- Verification:
  - Existing tests compile.
  - New helper names make assertion intent clear at call sites.

Task 3.2: Add semantic renderer attributes.

- Action: add stable attributes to approval and review HTML:
  - `data-section="plan-summary|approval-actions"`
  - `data-field="plan-id|operation|intent-digest|review-digest|requester|status"`
  - `data-action="approve|deny|cancel"`
  - Kubernetes review sections such as `objects`, `submitted-manifest`, `policy-findings`, `dry-run-results`, and `diff`
- Acceptance criteria:
  - Attributes describe domain structure, not styling.
  - Renderer tests can locate content and actions without asserting visible copy.
  - Visible text remains available for users and accessibility.
- Verification:
  - Renderer/endpoint tests that previously asserted headings, button copy, or CSS classes now assert semantic attributes unless the copy itself is the contract.

Task 3.3: Migrate high-value brittle tests first.

- Action: migrate approval-flow, gateway behavior, pre-execution gate, plan decode/build, and safety-flow tests away from prose assertions.
- Acceptance criteria:
  - Non-renderer approval-flow tests assert status, reason code, challenge state, grant state, audit payload, JSON shape, or domain objects.
  - Renderer, CLI, security redaction, OAuth/JWT, Kubernetes path/query, JSON field-name, and audit-contract tests may keep string assertions when the string is the explicit contract.
  - Tests use existing convention constants for tool names, routes, env vars, claims, audit events, Kubernetes kinds, and API versions where those constants exist.
- Verification:
  - `rg -n 'Approval URL:|Approval required\.|Refused:|was approved|same authenticated subject|pending plan changed|missing recorded evidence data' tests`
  - Remaining matches are renderer, CLI, security, external-protocol, or explicitly documented contract tests.

### Phase 4: Revise Test Guidance And Docs

Task 4.1: Revise `.agents/skills/writing-tests/SKILL.md`.

- Action: add an "Assertion Surface" section.
- Required guidance:
  - Assert behavior through typed status, reason code, state transitions, audit payloads, JSON structure, or domain objects.
  - Do not assert user-facing prose unless the test is specifically for rendering, CLI output, redaction text, or external protocol text.
  - Use convention constants for contract strings: tool names, JSON keys, env vars, routes, audit events, OAuth claims, Kubernetes kinds, and API versions.
  - Use helpers for unavoidable parsing, and make parsing format-based instead of label-text-based.
  - When adding a new result branch, add a reason code at the same boundary as the decision.
- Acceptance criteria:
  - Skill covers all current test projects, including RuntimeSafety, Observability, RunProfiles, and Safety E2E.
  - Skill distinguishes contract strings from presentation strings.
  - Skill does not encourage broad shared helper abstractions without repeated use.
- Verification:
  - Re-read the revised skill against at least one migrated test from each affected category.

Task 4.2: Verify README and docs drift.

- Action: inspect the README set that describes affected behavior and tests.
- Likely files to check:
  - `README.md`
  - `docs/devs-readme.md`
  - `docs/why-separated-plan-from-challenge.md`
  - `src/InfraGate.Approvals/README.md`
  - `src/InfraGate.McpGateway/README.md`
  - `src/InfraGate.McpServer/README.md`
  - `tests/InfraGate.McpGateway.Tests/README.md`
  - `tests/InfraGate.McpServer.Tests/README.md`
  - `tests/InfraGate.Safety.E2E.Tests/README.md`
  - consider adding `tests/InfraGate.RunProfiles.Tests/README.md` if the project remains undocumented
- Acceptance criteria:
  - Docs do not claim tests assert exact refusal/approval prose unless that is still intentional.
  - Docs mention reason-code/semantic-attribute assertions only where they explain real project conventions.
  - `docs/why-separated-plan-from-challenge.md` is updated if it still names exact refusal text as the behavioral contract.
  - No broad README rewrite is included unless real drift is found.
- Verification:
  - `rg --files | rg -i 'readme\.md$' | rg -v '^\.agents/' | rg -v '/(bin|obj)/' | sort`
  - `rg -n 'Approval URL:|Approval required\.|Refused:|pending plan changed|reason code|ReasonCode|data-section|data-field|data-action' README.md docs src tests`

### Phase 5: Verification And Rollout

Task 5.1: Run focused test suites.

- Action:
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
  - `dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj`
  - `dotnet test tests/InfraGate.Observability.Tests/InfraGate.Observability.Tests.csproj`
- Acceptance criteria:
  - All focused suites pass.
  - Any expected opt-in exclusions are called out explicitly.

Task 5.2: Run default solution verification.

- Action: `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"`
- Acceptance criteria:
  - Default non-opt-in suite passes.
  - If environment-specific failures occur, capture the exact failure and apply the repo's .NET build/test guidance before declaring the run blocked.

Task 5.3: Run static acceptance checks.

- Action:
  - `git diff --check`
  - `rg -n 'Message\.Contains\("Refused:|Contains\("Refused:' src`
  - `rg -n 'Approval URL:|Approval required\.|Refused:|was approved|same authenticated subject|pending plan changed|missing recorded evidence data' tests`
- Acceptance criteria:
  - No production branch depends on formatted user text.
  - No non-renderer approval-flow tests assert approval/refusal prose.
  - Remaining string assertions are contract constants, renderer/CLI/security assertions, or external protocol strings with clear intent.

## Risks And Mitigations

- Risk: reason-code sprawl.
  - Mitigation: add codes only for existing decision branches and keep them in the owning convention module.
- Risk: accidental public contract change.
  - Mitigation: preserve `Message` fields and MCP text output during the first pass; migrate tests before changing any user-visible copy.
- Risk: Generic Approval Core learns Kubernetes details.
  - Mitigation: generic result records carry opaque codes; adapter-specific code names stay in adapter conventions.
- Risk: constructor churn across many result records.
  - Mitigation: prefer factory methods, overloads, or defaulted properties that keep call-site changes small.
- Risk: test helper overreach.
  - Mitigation: keep helpers local to affected test projects unless repeated use proves a shared helper is worth it.
- Risk: hidden DI fixture drift.
  - Mitigation: check all gateway test-host creation paths after service/result changes, especially fixture-specific service registrations.
- Risk: documentation over-editing.
  - Mitigation: update only docs with real drift, plus the `writing-tests` skill because it is the durable testing rule.

## Definition Of Done

- Production code no longer branches on approval/refusal prose.
- Approval, gateway, and Kubernetes plan outcomes expose stable reason codes or typed statuses.
- High-value approval-flow tests assert structure and state instead of user-facing copy.
- Renderer tests use semantic attributes except where visible text is the behavior under test.
- `.agents/skills/writing-tests/SKILL.md` explains the assertion-surface rule and current test-project map.
- README/docs that describe the affected test behavior are checked and updated only where drift exists.
- Focused suites and default non-opt-in solution tests pass, or any blocker is recorded with exact command output.
