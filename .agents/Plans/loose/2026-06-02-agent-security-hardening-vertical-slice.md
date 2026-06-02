# Implementation Plan: Agent-Security Hardening Vertical Slice

> Status: proposed (2026-06-02). Planning document only; no implementation has started.
>
> This plan supersedes [`future-guardrails-extensions.md`](./future-guardrails-extensions.md) for
> model-visible content protection. It also replaces the earlier Azure-first recommendation:
> **Azure AI Content Safety is not a dependency or target adapter for this slice.**

## Objective

InfraGate already has a strong deterministic mutation boundary: narrow OAuth scopes, hidden destructive
tools, Planner operation validation, digest-bound plans, out-of-band human approval, freshness checks,
and Kubernetes RBAC. Keep those controls.

The missing layer is protection for **untrusted Kubernetes text before an agent model consumes it**.
Kubernetes events, diagnostics, log excerpts, object metadata, Observer anomaly evidence, and Planner
deep-dive tool results are all data, but an LLM can treat hostile text inside that data as instructions.

Build this vertical slice:

> Add one deep `IModelVisibleContentGuard` seam to `InfraGate.AgentGuardrails`; preserve the gateway's
> existing scanner as an independent fast first pass; wrap Microsoft's open-source Agent Governance
> Toolkit (AGT) detector behind an adapter; then add a local OSS semantic classifier only after a
> measured bakeoff.

Keep prompt tuning, approval-flow changes, broad AGT policy-engine migration, Azure integration, and
classifier-dependent Kubernetes execution outside this slice.

## Verified repository fit

| Existing surface | Current behavior | Direction |
|---|---|---|
| `src/InfraGate.McpGateway/Guardrails/Scanning/PromptInjectionGuard.cs` | Five lexical categories: ignore instructions, reveal prompts, tool use, secret exfiltration, authority override. | Keep unchanged as an independently owned gateway boundary. |
| `src/InfraGate.McpGateway/Guardrails/Scanning/PromptInjectionGuard.Regex.cs` | Decodes full and embedded Base64 payloads before scanning. | Keep as cheap first pass and fallback. |
| `src/InfraGate.McpGateway/Guardrails/Scanning/PromptInjectionGuard.Scanning.cs` | Scans nested dictionaries, arrays, `JsonElement`, and `JsonNode` leaf values. | Keep, but do not treat leaf scanning as complete semantic protection. |
| `src/InfraGate.McpGateway/Guardrails/Sanitization/PromptInjectionGuard.Sanitization.cs` | Redacts suspicious JSON leaves or text lines and strips manifest echoes. | Keep at the public MCP boundary. |
| `src/InfraGate.McpGateway/Guardrails/GuardedToolRunner.cs` | Scans requests, sanitizes responses, and writes guardrail audit events inline. | Keep behavior; isolate audit-write failures in a companion hardening task. |
| `src/InfraGate.AgentGuardrails/README.md` | Existing deep module for shared tool-call middleware and metric vocabulary. | Extend this module with the one model-visible content seam. |
| `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs` | Shared Observer/Planner agent construction and function decoration point. | Add shared model-visible tool-result decoration here after a compatibility spike. |
| `src/InfraGate.Observer/Cycle/Workflow/SnapshotExecutor.cs` | Serializes `SnapshotDocument` and sends JSON directly as a user `ChatMessage`. | Guard the complete serialized snapshot before sending it to the agent. |
| `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs` | Serializes `AnomalyReport` and sends JSON directly to `agent.RunAsync`. | Guard the complete serialized anomaly before invoking the model. |
| `src/InfraGate.Planner/Llm/AskObserverTool.cs` | Returns Observer deep-dive results to the Planner model as tool output. | Cover through the shared function-result decorator; add a focused regression test. |
| `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Planner/Program.cs` | Compose `InfraGate.AgentGuardrails`; both hard-code `RequireHttpsMetadata = false`. | Wire the new seam here; fix HTTPS metadata policy as a companion production task. |
| `src/InfraGate.Observer/Llm/ChatClientFactory.cs`, `src/InfraGate.Planner/Llm/ChatClientFactory.cs` | Agent hosts currently execute only the OpenRouter path; reserved providers are not implemented for native tool calling. | Add production route policy separately; do not couple provider expansion to this slice. |
| `deploy/run-profiles.yaml` | Local profiles use `openrouter/free`; a production profile exists but does not currently configure agents. | Keep local demo defaults; reject free routes in Production when agents are enabled. |

The current gateway scanner reduces exposure because agent MCP calls pass through the gateway. It does
not close the model-ingestion gap: Observer snapshot JSON and Planner anomaly JSON are assembled after
gateway calls, split-field attacks can become meaningful only after aggregation, and a lexical scanner
cannot reliably catch paraphrased or multilingual attacks.

## Upstream dependency posture

### Microsoft Agent Governance Toolkit: adopt selectively

Official sources:

- [Microsoft Agent Governance Toolkit repository](https://github.com/microsoft/agent-governance-toolkit)
- [.NET SDK README](https://github.com/microsoft/agent-governance-toolkit/blob/main/agent-governance-dotnet/README.md)
- [Published `Microsoft.AgentGovernance` package](https://www.nuget.org/packages/Microsoft.AgentGovernance)
- [Toolkit limitations](https://github.com/microsoft/agent-governance-toolkit/blob/main/docs/LIMITATIONS.md)

Verified on 2026-06-02:

- AGT is MIT-licensed, public preview software. Core governance works offline; Azure integrations are
  optional.
- The published .NET core package is `Microsoft.AgentGovernance` `3.7.0`. The published companion
  packages are `Microsoft.AgentGovernance.Extensions.Microsoft.Agents` and
  `Microsoft.AgentGovernance.Extensions.ModelContextProtocol`.
- The prompt detector is in the core package under the `AgentGovernance.Security` namespace. There is
  **no separately published `Microsoft.AgentGovernance.Security` NuGet package**.
- AGT's detector is a useful deterministic layer: pattern matching, Unicode normalization, encoding
  analysis, and prompt-injection category detection.
- AGT's own limitations are explicit: action governance is not reasoning governance; indirect prompt
  injection can still corrupt model reasoning. Do not present AGT as a semantic classifier.

Adoption rule:

> Pin and wrap `Microsoft.AgentGovernance` behind InfraGate's seam. Do not expose AGT types to Observer
> or Planner, and do not replace InfraGate's approval, scope, adapter, or execution controls with AGT's
> general-purpose policy engine during this slice.

### Local semantic classifier: evaluate, then select

Candidate sources:

- [Protect AI LLM Guard](https://protectai.github.io/llm-guard/) and its
  [Prompt Injection scanner](https://protectai.github.io/llm-guard/input_scanners/prompt_injection/)
- [Protect AI DeBERTa prompt-injection model card](https://huggingface.co/protectai/deberta-v3-base-prompt-injection-v2)
- [InjecGuard / PIGuard repository](https://github.com/safolab-wisc/injecguard) and
  [ACL 2025 paper](https://aclanthology.org/2025.acl-long.1468/)
- [NVIDIA garak](https://github.com/NVIDIA/garak)
- [OWASP LLM Prompt Injection Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/LLM_Prompt_Injection_Prevention_Cheat_Sheet.html)

Start with Protect AI LLM Guard as the integration baseline because it is local, permissively licensed,
and exposes a documented input scanner. Benchmark InjecGuard/PIGuard as the challenger. Select the
runtime model from measured recall, false positives, latency, resource use, model-license review, and
operational simplicity. Do not commit model weights to this repository.

## Decided architecture

### One consumer-facing seam

`InfraGate.AgentGuardrails` owns one primary interface:

```csharp
public interface IModelVisibleContentGuard
{
    Task<ModelVisibleContentDecision> EvaluateAsync(
        ModelVisibleContent content,
        CancellationToken cancellationToken);
}
```

Minimal input:

```csharp
public sealed record ModelVisibleContent(
    string Text,
    ModelVisibleContentSource Source,
    string AgentName,
    string? CorrelationId = null,
    string? ToolName = null);
```

Minimal decision:

```csharp
public sealed record ModelVisibleContentDecision(
    ModelVisibleContentAction Action,
    string Text,
    IReadOnlyList<string> Categories,
    string Reason);
```

Actions:

| Action | Meaning |
|---|---|
| `Allow` | Pass the text to the model unchanged. |
| `Redact` | Pass a sanitized replacement to the model. |
| `Quarantine` | Do not pass the original text; pass a bounded neutral placeholder and record metadata for investigation. Do not persist raw hostile content by default. |
| `BlockModelIngestion` | Stop that model-ingestion branch before the LLM call. |

Observer and Planner receive only `IModelVisibleContentGuard`. They must not know about AGT types,
local HTTP endpoints, classifier models, timeout rules, fallback policy, chunking, or audit details.

### Internal pipeline

```text
model-visible text
  -> full-document size bound
  -> AGT deterministic adapter (fast, local, pinned)
  -> optional local semantic-classifier adapter (local sidecar)
  -> policy reduction
  -> structured log + metric (+ host audit adapter where durable audit is enabled)
  -> Allow | Redact | Quarantine | BlockModelIngestion
```

Implement adapters as separate projects so external dependencies remain hidden:

```text
src/InfraGate.AgentGuardrails/
  IModelVisibleContentGuard.cs
  ModelVisibleContent.cs
  ModelVisibleContentDecision.cs
  ModelVisibleContentAction.cs
  ModelVisibleContentSource.cs
  CompositeModelVisibleContentGuard.cs
  AllowAllModelVisibleContentGuard.cs

src/InfraGate.AgentGuardrails.AgentGovernanceToolkit/
  AgentGovernanceToolkitContentGuard.cs
  AgentGovernanceToolkitContentGuardServiceCollectionExtensions.cs

src/InfraGate.AgentGuardrails.LocalClassifier/       # add only after bakeoff go/no-go
  LocalClassifierContentGuard.cs
  LocalClassifierOptions.cs
  LocalClassifierContentGuardServiceCollectionExtensions.cs
```

All adapters implement the same `IModelVisibleContentGuard`; no public interface per classifier stage.
The composite guard is the configured runtime implementation.

### Independent gateway boundary

Do **not** move `PromptInjectionGuard` out of `InfraGate.McpGateway`, and do not make the gateway depend
on `InfraGate.AgentGuardrails`. The two controls have different ownership:

- `InfraGate.McpGateway`: protects every MCP client and sanitizes public gateway output.
- `InfraGate.AgentGuardrails`: protects Observer and Planner immediately before content reaches an LLM.

The overlap is intentional defense in depth. Tests should prove both layers remain independently active.

### Boundary policy

| Boundary | Guard input | Production unavailable-classifier policy | Suspicious-content result |
|---|---|---|---|
| Observer snapshot | Complete serialized `SnapshotDocument` before `ChatMessage` | Fail closed for model ingestion after AGT fallback policy is exhausted. | Redact/quarantine when safe; otherwise skip that namespace branch. |
| Planner anomaly | Complete serialized `AnomalyReport` before `agent.RunAsync` | Fail closed for model ingestion after AGT fallback policy is exhausted. | Redact/quarantine the model copy; keep the typed report unchanged for deterministic validation and audit correlation. |
| Agent tool result | Function result immediately before the Agent Framework returns it to the model | Fail closed by returning a bounded blocked/quarantined tool-result marker. | Never expose the original hostile text to the model. |
| Approval-bound execution | Typed plan envelope, grant, digest, freshness, adapter policy checks | Not classifier-dependent. | Continue to use deterministic execution gates. |

Development may opt into a metered `deterministic-only` degraded mode when the semantic sidecar is
unavailable. Production must not silently fail open.

### Configuration direction

Use one bound section:

```text
InfraGate:AgentGuardrails:ModelVisibleContent
```

Expected settings:

```text
Enabled
SemanticClassifierEnabled
LocalClassifierBaseUrl
RequestTimeoutMilliseconds
MaximumInputCharacters
UnavailableBehavior
QuarantinePlaceholder
```

Coordinate this with
[`2026-06-02-config-appsettings-first-single-env-refactor.md`](./2026-06-02-config-appsettings-first-single-env-refactor.md).
Do not add a new parallel environment-variable mapping scheme while that refactor is active. Keep local profiles
opt-in until the semantic sidecar is packaged and measured.

## Dependency graph

```text
0  refresh CodeGraph + record source baseline
│
A  AGT published-package spike + adversarial corpus baseline (go/no-go)
│
B  core IModelVisibleContentGuard contracts + composite + metrics
│  └── B3 AGT deterministic adapter (pinned Microsoft.AgentGovernance 3.7.0)
│
C  first complete vertical slice
│  ├── Observer snapshot guard
│  ├── Planner anomaly guard
│  └── shared tool-result guard decoration
│
D  local OSS semantic classifier bakeoff
│  └── selected local sidecar adapter + opt-in runtime wiring
│
E  AGT auxiliary evaluation: prompt-defense CI + MCP extension shadow assessment
│
F  companion production hardening: provider policy, HTTPS metadata, audit-write isolation
│
G  docs, ADR, eval reporting, scheduled red-team checks
```

## Task list

### Phase 0: Planning hygiene

#### Task 0.1: Refresh CodeGraph and onboarding expectations

**Description:** Rebuild the CodeGraph index before implementation and update the onboarding skill's stale health
expectation. The current index reports 671 files / 9067 nodes, while the skill still expects about 260 files /
2670 nodes. One indexed lookup also referenced the historical `GatewayAgentMcpToolset.cs`; the current file is
`src/InfraGate.AgentMcp/AgentMcpToolset.cs`.

**Acceptance criteria:**
- [ ] `codegraph_status` reports an index rebuilt from the current working tree.
- [ ] `codegraph_context` resolves `AgentMcpToolset`, `SnapshotExecutor`, `DecideExecutor`, and `ToolCallingAgentFactory` to current paths.
- [ ] `.agents/skills/repo-onboarding/SKILL.md` uses current approximate counts or avoids brittle exact expectations.

**Verification:**
- [ ] Run `codegraph_status`.
- [ ] Run `codegraph_context` for the model-visible content slice and inspect paths.

**Dependencies:** None

**Files likely touched:**
- `.agents/skills/repo-onboarding/SKILL.md`
- `.codegraph/**` generated index files, if tracked by local tooling

**Estimated scope:** Small

### Phase A: De-risk AGT and define the measurable target

#### Task A1: Spike the published AGT .NET API

**Description:** Create a disposable spike or test-only branch that restores the published
`Microsoft.AgentGovernance` `3.7.0` package and exercises `AgentGovernance.Security.PromptInjectionDetector`.
Verify the published API, target-framework compatibility with `net10.0`, transitive dependencies, offline
operation, category output, Unicode normalization, encoding handling, and latency. Compare the core detector with
the optional Microsoft Agents extension, but do not adopt the extension unless it removes real integration code
without bypassing InfraGate's seam.

**Acceptance criteria:**
- [ ] The package restores and executes without Azure credentials or network calls at runtime.
- [ ] Spike findings record the exact published detector API and any mismatch with `main` README examples.
- [ ] The go/no-go records whether InfraGate needs only the core detector or also
  `Microsoft.AgentGovernance.Extensions.Microsoft.Agents`.

**Verification:**
- [ ] `dotnet restore` and `dotnet test` succeed for the disposable spike.
- [ ] Run detector cases for clean Kubernetes text, lexical injection, Unicode obfuscation, encoded injection,
  and delimiter injection.

**Dependencies:** Task 0.1

**Files likely touched:** disposable spike only; no production files

**Estimated scope:** Small

#### Task A2: Add the adversarial regression corpus and baseline runner

**Description:** Add a repo-owned JSONL corpus and a small deterministic runner under the guardrail test project.
The corpus is the contract for the slice: clean Kubernetes text plus lexical, paraphrased, multilingual,
split-field, Unicode, zero-width, Base64, hex, delimiter, prompt-leak, tool-coercion, and secret-exfiltration
cases. Record expected minimum action, not one brittle implementation-specific category string. Baseline the
existing gateway scanner and the AGT spike detector separately.

**Acceptance criteria:**
- [ ] Corpus entries carry `id`, `source`, `text`, `expectedMinimumAction`, and `tags`.
- [ ] Clean Kubernetes fixtures include ordinary event messages, diagnostics, rollout status, labels, and bounded log excerpts.
- [ ] Baseline report records recall by tag, false positives, and p50/p95 latency for gateway scanner and AGT separately.
- [ ] Corpus text contains no real credentials, tokens, or production data.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] Save the generated baseline summary as a review artifact or append the measured table to this plan.

**Dependencies:** Task A1

**Files likely touched:**
- `tests/InfraGate.AgentGuardrails.Tests/TestData/model-visible-content-corpus.jsonl`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/ModelVisibleContentCorpusTests.cs`
- optional `tools/guardrail-eval/**`

**Estimated scope:** Medium

### Checkpoint A: human go/no-go

- [ ] Published AGT package behavior is measured, not inferred from documentation.
- [ ] Corpus coverage is reviewed before implementation.
- [ ] Confirm AGT core adapter adoption.
- [ ] Confirm that Azure remains out of scope.

### Phase B: Deep-module foundation

#### Task B1: Add the core model-visible content contract

**Description:** Extend `InfraGate.AgentGuardrails` with the single consumer-facing
`IModelVisibleContentGuard` interface, input/decision records, actions, sources, an allow-all implementation for
explicit development/test use, and an ordered composite implementation. Keep provider types out of the core
project. Define reduction rules once: the strongest action wins, redacted text is passed forward stage by stage,
and raw hostile content is never logged.

**Acceptance criteria:**
- [ ] Consumers need one dependency and one `EvaluateAsync` call.
- [ ] Composite reduction order is deterministic and unit-tested.
- [ ] `Quarantine` and `BlockModelIngestion` never return the original text.
- [ ] No AGT, HTTP, model, or sidecar type appears in the core public API.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] `dotnet build InfraGate.slnx --configuration Release`

**Dependencies:** Checkpoint A

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/IModelVisibleContentGuard.cs`
- `src/InfraGate.AgentGuardrails/ModelVisibleContent*.cs`
- `src/InfraGate.AgentGuardrails/CompositeModelVisibleContentGuard.cs`
- `src/InfraGate.AgentGuardrails/AllowAllModelVisibleContentGuard.cs`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/*`

**Estimated scope:** Medium

#### Task B2: Add bounded telemetry and structured decision logging

**Description:** Extend `AgentGuardrailMetrics` with model-visible decision count, degraded-mode count, and
evaluation-latency histogram. Use bounded tags only: agent name, source, action, stage/provider id, category, and
reason. Do not tag raw text, correlation IDs, resource names, or free-form model output. Add structured logs with
the same metadata. Decide during implementation whether durable Observer/Planner outbox adapters are required in
this slice; if added, keep them behind internal registration support rather than expanding the agent-facing API.

**Acceptance criteria:**
- [ ] Metrics distinguish `allow`, `redact`, `quarantine`, `block_model_ingestion`, and degraded evaluation.
- [ ] Logs and metrics contain classifier version or detector version where available.
- [ ] Raw hostile text is absent from logs, metrics, and default quarantine handling.
- [ ] Telemetry or audit-write failure never exposes content that the guard decided to block.

**Verification:**
- [ ] Meter-listener unit tests assert instrument names and bounded tags.
- [ ] Capturing-logger tests assert raw corpus payloads are not logged.

**Dependencies:** Task B1

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/AgentGuardrailConventions.cs`
- `src/InfraGate.AgentGuardrails/AgentGuardrailMetrics.cs`
- `src/InfraGate.AgentGuardrails/README.md`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/AgentGuardrailMetricsTests.cs`

**Estimated scope:** Medium

#### Task B3: Add the pinned AGT deterministic adapter

**Description:** Add `InfraGate.AgentGuardrails.AgentGovernanceToolkit`, reference
`Microsoft.AgentGovernance` `3.7.0`, and implement an adapter from AGT detector output to InfraGate decisions.
Keep this deterministic stage local and fast. Do not introduce AGT's policy kernel, OPA/Rego, Cedar, Azure
monitoring, or MCP extension into Observer/Planner wiring in this task.

**Acceptance criteria:**
- [ ] Adapter implements only `IModelVisibleContentGuard`.
- [ ] Package version is pinned.
- [ ] Tests prove offline use with no Azure configuration.
- [ ] Corpus tests record exactly which cases AGT catches and misses.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests.csproj`
- [ ] `dotnet build InfraGate.slnx --configuration Release`

**Dependencies:** Tasks B1, B2

**Files likely touched:**
- new `src/InfraGate.AgentGuardrails.AgentGovernanceToolkit/**`
- new `tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests/**`
- `InfraGate.slnx`

**Estimated scope:** Medium

### Checkpoint B: deep module complete

- [ ] One public runtime seam protects consumers.
- [ ] AGT is hidden behind an adapter and pinned.
- [ ] Existing tool-call guardrail tests remain green.
- [ ] No gateway dependency on `InfraGate.AgentGuardrails` has been introduced.

### Phase C: First complete runtime vertical slice

#### Task C1: Guard Observer snapshot ingestion

**Description:** Inject `IModelVisibleContentGuard` into `SnapshotExecutor`. Evaluate the complete serialized
`SnapshotDocument` before constructing the user `ChatMessage`. Preserve snapshot fetching and gateway
sanitization. For a blocked namespace branch, do not call the LLM; emit a bounded branch result so the fan-in
workflow completes without hanging.

**Acceptance criteria:**
- [ ] Allowed snapshot reaches the agent unchanged.
- [ ] Redacted/quarantined snapshot reaches the agent only as safe text.
- [ ] Blocked snapshot does not reach the model and does not hang the workflow fan-in.
- [ ] Failure policy is explicit and tested.

**Verification:**
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- [ ] Add focused workflow tests for all four actions.

**Dependencies:** Task B3

**Files likely touched:**
- `src/InfraGate.Observer/Cycle/Workflow/SnapshotExecutor.cs`
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`
- `src/InfraGate.Observer/Program.cs`
- `tests/InfraGate.Observer.Tests/UnitTests/ObservationCycleRunnerTests.cs`

**Estimated scope:** Medium

#### Task C2: Guard Planner anomaly ingestion

**Description:** Inject `IModelVisibleContentGuard` into `DecideExecutor`. Evaluate the complete serialized
`AnomalyReport` before `agent.RunAsync`. Pass only the guarded string to the LLM while retaining the original typed
report for deterministic validation, audit correlation, and proposal construction.

**Acceptance criteria:**
- [ ] Allowed anomaly behavior is unchanged.
- [ ] Redaction never mutates the typed `AnomalyReport`.
- [ ] Quarantined or blocked anomaly text never reaches the LLM.
- [ ] Blocked ingestion drops the LLM decision path without calling `propose_plan`.

**Verification:**
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
- [ ] Add focused `DecideExecutor` tests for all four actions using hand-written fake guards.

**Dependencies:** Task B3

**Files likely touched:**
- `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs`
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs`
- `src/InfraGate.Planner/Program.cs`
- `tests/InfraGate.Planner.Tests/UnitTests/WorkflowExecutorTests.cs`

**Estimated scope:** Medium

#### Task C3: Guard every agent-visible tool result at the shared factory

**Description:** Extend the shared function decoration in `ToolCallingAgentFactory` so actual tool output is
evaluated immediately before Agent Framework returns it to the model. First verify that MCP tools are represented
as `AIFunction` instances in the current `ModelContextProtocol` package; if not, add the narrowest adapter at the
toolset boundary without making `InfraGate.AgentMcp` depend on guardrail policy. Cover `AskObserverTool` through
the same decorator.

**Acceptance criteria:**
- [ ] Observer dynamic MCP tool results are guarded.
- [ ] Planner dynamic MCP tool results are guarded.
- [ ] `AskObserverTool` results are guarded without a bespoke second policy implementation.
- [ ] A blocked tool result returns a bounded safe marker and never the original output.
- [ ] Existing invocation counting and tool allow-list blocking still work.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentLlm.Tests/InfraGate.AgentLlm.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`

**Dependencies:** Task B3

**Files likely touched:**
- `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs`
- `src/InfraGate.AgentGuardrails/*ToolResult*.cs`
- `tests/InfraGate.AgentLlm.Tests/UnitTests/ToolCallingAgentFactoryTests.cs`
- `tests/InfraGate.Planner.Tests/UnitTests/AskObserverToolTests.cs`

**Estimated scope:** Medium

#### Task C4: Wire explicit development and production behavior

**Description:** Register the AGT-backed composite in Observer and Planner. Add bound
`InfraGate:AgentGuardrails:ModelVisibleContent` options with an explicit unavailable behavior. Keep semantic
classification disabled until Phase D. Coordinate configuration transport with the active appsettings-first
refactor rather than adding new mapping glue.

**Acceptance criteria:**
- [ ] Observer and Planner resolve one `IModelVisibleContentGuard`.
- [ ] AGT deterministic evaluation is enabled by default when agents run.
- [ ] No Azure key, endpoint, container, or SDK is required.
- [ ] Development and Production unavailable behavior is explicit and startup-validated.

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release`
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`

**Dependencies:** Tasks C1, C2, C3

**Files likely touched:**
- `src/InfraGate.Observer/Program.cs`
- `src/InfraGate.Planner/Program.cs`
- `src/InfraGate.AgentGuardrails/*Options*.cs`
- `deploy/run-profiles.yaml`
- matching RunProfiles parser/renderer tests, according to the active configuration refactor state

**Estimated scope:** Medium

### Checkpoint C: deterministic model-ingestion protection shipped

- [ ] Observer snapshot JSON, Planner anomaly JSON, and dynamic tool outputs pass through one deep seam.
- [ ] Gateway scanner remains independently active.
- [ ] A corpus injection cannot reach an LLM through any of the three known paths.
- [ ] Approval-bound execution behavior is unchanged.

### Phase D: Local OSS semantic classifier

#### Task D1: Run the local-classifier bakeoff

**Description:** Build disposable local sidecars for Protect AI LLM Guard and InjecGuard/PIGuard. Run the Phase A
corpus and a representative clean Kubernetes sample set. Record recall by attack tag, clean false-positive rate,
p50/p95 latency, warm-up time, memory use, CPU use, image size, model provenance, model hash, and license review.
Do not select a model from headline benchmark claims alone.

**Acceptance criteria:**
- [ ] Both candidates run locally without Azure or another paid API.
- [ ] Results distinguish lexical-only, semantic paraphrase, multilingual, encoded, and split-field cases.
- [ ] The selected candidate and rejected candidate rationale are appended to this plan.
- [ ] Model artifact source, revision, checksum, and license are recorded.

**Verification:**
- [ ] Run the corpus evaluation tool against both local sidecars.
- [ ] Review the generated comparison table with a human before production wiring.

**Dependencies:** Checkpoint C

**Files likely touched:** disposable benchmark assets under `tools/guardrail-eval/**`

**Estimated scope:** Medium

#### Task D2: Add the selected local-classifier adapter

**Description:** Add `InfraGate.AgentGuardrails.LocalClassifier` as an HTTP adapter for the selected sidecar.
Enforce request-size bounds, timeout, cancellation, health/readiness, classifier-version capture, latency metrics,
and explicit unavailable behavior. Keep retry count low or zero for synchronous model ingestion; a circuit breaker
may suppress repeated calls during an outage, but it must not silently weaken Production policy.

**Acceptance criteria:**
- [ ] Adapter implements only `IModelVisibleContentGuard`.
- [ ] Timeout, cancellation, malformed response, unavailable sidecar, and oversized input behavior are tested.
- [ ] Production policy fails closed for model ingestion after configured fallback.
- [ ] Development degraded mode is explicit, metered, and opt-in.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.LocalClassifier.Tests/InfraGate.AgentGuardrails.LocalClassifier.Tests.csproj`
- [ ] Run integration tests against the actual local sidecar container; do not mock the classifier HTTP contract.

**Dependencies:** Task D1

**Files likely touched:**
- new `src/InfraGate.AgentGuardrails.LocalClassifier/**`
- new `tests/InfraGate.AgentGuardrails.LocalClassifier.Tests/**`
- `InfraGate.slnx`

**Estimated scope:** Medium

#### Task D3: Package and opt into the semantic sidecar

**Description:** Package the selected classifier as a separately versioned local container. Add opt-in Compose and
Run Profile wiring for local evaluation first. After measured acceptance, enable it in the production profile with
explicit resource limits and readiness checks. Observer and Planner call only the adapter base URL; they do not
embed Python/model dependencies.

**Acceptance criteria:**
- [ ] Sidecar image is pinned by digest or immutable tag.
- [ ] Model revision and checksum are pinned.
- [ ] Compose health check and resource limits exist.
- [ ] Observer/Planner images remain .NET-only and do not contain model weights.
- [ ] Run Profile validation catches semantic-classifier-enabled profiles without a base URL.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] Local Compose smoke: stop the sidecar and verify the configured degraded/fail-closed behavior.

**Dependencies:** Task D2

**Files likely touched:**
- `deploy/run-profiles.yaml`
- Compose files under `deploy/**`
- sidecar packaging under `tools/guardrail-classifier/**`
- RunProfiles schema/rendering/tests in their post-refactor form

**Estimated scope:** Medium

### Checkpoint D: semantic layer measured and operational

- [ ] AGT remains the deterministic first stage.
- [ ] Semantic sidecar selection is backed by repo-specific measurements.
- [ ] Sidecar outage policy is demonstrated.
- [ ] No cloud safety dependency exists.

### Phase E: Evaluate AGT auxiliary capabilities without widening ownership

#### Task E1: Add prompt-defense static analysis to CI

**Description:** Evaluate AGT's prompt-defense evaluator against embedded prompt assets and configuration files.
If it adds signal, run it as a fast CI check. Keep this as analysis tooling, not runtime policy. Add NVIDIA garak
as an opt-in or scheduled red-team job once a local model/provider target is stable.

**Acceptance criteria:**
- [ ] CI analysis scans prompt assets and fails only on reviewed actionable categories.
- [ ] False positives are documented with narrow suppressions.
- [ ] Scheduled red-team output is uploaded as an artifact.

**Verification:**
- [ ] Run the analysis locally against `src/InfraGate.Observer/Prompts` and `src/InfraGate.Planner/Prompts`.
- [ ] Validate the CI workflow syntax and artifact upload.

**Dependencies:** Checkpoint C

**Files likely touched:**
- `.github/workflows/ci.yml`
- optional new `.github/workflows/agent-security-redteam.yml`
- prompt-analysis configuration under `tools/guardrail-eval/**`

**Estimated scope:** Medium

#### Task E2: Assess the AGT MCP extension in shadow mode

**Description:** Evaluate `Microsoft.AgentGovernance.Extensions.ModelContextProtocol` against the private
`InfraGate.McpServer` builder. Determine whether its tool-definition scan catches poisoned descriptions or
metadata that InfraGate does not already test. Do not adopt runtime policy enforcement if it duplicates downstream
auth filters, gateway scopes, approval semantics, or response sanitization without measurable value.

**Acceptance criteria:**
- [ ] Findings document overlap and unique value.
- [ ] Any adopted scan runs in startup/CI shadow mode first.
- [ ] Gateway and Generic Approval Core remain the authoritative execution boundaries.

**Verification:**
- [ ] Add or run a test MCP server with intentionally poisoned tool descriptions.
- [ ] Confirm existing `DownstreamAuthFilter` and `ToolExceptionFilter` behavior remains unchanged.

**Dependencies:** Task A1

**Files likely touched:** spike first; production files only after a separate go/no-go

**Estimated scope:** Small

### Phase F: Companion production hardening

#### Task F1: Enforce production-safe agent provider and OAuth metadata policy

**Description:** Add a production validator for Observer, Planner, and Executor agent-tier settings. Reject free
or demo OpenRouter routes (`openrouter/free`, `:free`) in Production, require explicit governed model selection,
and stop hard-coding `RequireHttpsMetadata = false`. Local Keycloak profiles remain Development-only and may
explicitly disable HTTPS metadata checks.

**Acceptance criteria:**
- [ ] Production agent hosts reject free/demo model routes.
- [ ] Production agent hosts require HTTPS metadata validation.
- [ ] Development profiles still run with local Keycloak and local demo models.
- [ ] No paid provider is required by the architecture; OpenRouter organization guardrails remain an optional
  provider-level overlay, not the InfraGate-owned boundary.

**Verification:**
- [ ] Add validator tests for Development and Production cases.
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Executor.Tests/InfraGate.Executor.Tests.csproj`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`

**Dependencies:** Coordinate with the appsettings-first configuration refactor; otherwise independent of Phase D

**Files likely touched:**
- `src/InfraGate.Observer/Program.cs`, `ObserverOptions.cs`
- `src/InfraGate.Planner/Program.cs`, `PlannerOptions.cs`
- `src/InfraGate.Executor/Program.cs`, `ExecutorOptions.cs`
- `src/InfraGate.RuntimeSafety/**`
- `deploy/run-profiles.yaml`

**Estimated scope:** Medium; split per host if implementation exceeds five files

#### Task F2: Isolate gateway guardrail audit-write failures

**Description:** Harden `GuardedToolRunner` so a guardrail audit storage failure cannot replace a sanitized
response, suppress a warning, or disrupt a legitimate downstream tool call. Log and meter audit persistence
failure separately. Preserve the scan/sanitize result.

**Acceptance criteria:**
- [ ] Request-audit write failure does not prevent the downstream tool call.
- [ ] Response-audit write failure does not expose unsanitized content.
- [ ] Audit storage failure is logged and metered.

**Verification:**
- [ ] Add throwing-audit-store tests in `GuardedToolRunnerTests`.
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

**Dependencies:** Independent; may be implemented early

**Files likely touched:**
- `src/InfraGate.McpGateway/Guardrails/GuardedToolRunner.cs`
- gateway guardrail metric/logging files
- `tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs`

**Estimated scope:** Small

### Phase G: Documentation, architecture decision, and release checks

#### Task G1: Record the architecture and operating contract

**Description:** Add an ADR and update the canonical glossary, architecture diagram, configuration reference, and
module READMEs. Document that model-visible content protection is distinct from gateway sanitization and from
approval-bound execution. Record AGT public-preview status, pinned version policy, local-classifier model
provenance, and outage behavior.

**Acceptance criteria:**
- [ ] `CONTEXT.md` defines **Model-Visible Content Guard**, **Model-Visible Content**, and **Quarantine**.
- [ ] ADR explains why the gateway scanner remains independently owned.
- [ ] Docs state that the semantic sidecar is local OSS and Azure is not required.
- [ ] Configuration docs match generated Run Profile output.

**Verification:**
- [ ] Run the `verify-readme-docs` skill.
- [ ] Check links and `dotnet run --project src/InfraGate.RunProfiles -- validate`.

**Dependencies:** Checkpoint D, Task F1

**Files likely touched:**
- `CONTEXT.md`
- new `docs/adr/00xx-model-visible-content-guard.md`
- `docs/architecture.md`
- `docs/configuration.md`
- `src/InfraGate.AgentGuardrails/README.md`
- relevant Observer/Planner READMEs

**Estimated scope:** Medium

#### Task G2: Add final CI and smoke gates

**Description:** Make the fast deterministic corpus part of pull-request CI. Keep sidecar integration tests in a
separate Docker-capable tier. Add a smoke scenario with hostile Kubernetes text that proves the model never sees
the raw payload while a legitimate approval-bound plan remains governed by the unchanged deterministic gates.

**Acceptance criteria:**
- [ ] PR CI runs the deterministic corpus without external services.
- [ ] Docker integration tier runs the actual local classifier sidecar.
- [ ] Smoke evidence shows redaction/quarantine/block behavior and unchanged approval safety properties.

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release`
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] Run the new Docker classifier integration tier.
- [ ] Run the targeted hostile-text smoke scenario.

**Dependencies:** Tasks D3, F2, G1

**Files likely touched:**
- `.github/workflows/ci.yml`
- optional classifier integration workflow
- targeted test project(s)

**Estimated scope:** Medium

## Parallelization opportunities

- After Checkpoint A, Tasks B1 and F2 can proceed independently.
- After Task B3, Tasks C1 and C2 can proceed in parallel; C3 is shared-factory work and should merge before final
  runtime wiring in C4.
- Tasks D1 and E1 can proceed in parallel after Checkpoint C.
- Task E2 is an isolated shadow assessment and must not block the model-visible vertical slice.
- Task F1 should coordinate with the active appsettings-first configuration plan to avoid duplicate configuration
  churn.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| AGT is public preview and its `main` README may lead the published NuGet API. | Medium | Spike the pinned published `3.7.0` package before production references; isolate it behind an adapter. |
| Deterministic scanners are oversold as semantic protection. | High | Keep AGT as stage one only; measure a local semantic stage on repo-specific attacks. |
| Local classifier outage silently weakens Production protection. | High | Explicit fail-closed Production policy, metered Development degraded mode, readiness checks, outage smoke test. |
| Guarding tool results in the shared factory changes Agent Framework return serialization. | High | Spike actual `AIFunction`/MCP tool behavior first; add focused factory tests before wiring hosts. |
| Blocking an Observer namespace branch hangs workflow fan-in. | Medium | Emit a bounded branch result and test full workflow completion. |
| Quarantine becomes a raw hostile-content retention store. | Medium | Do not persist raw content by default; retain bounded metadata and digest only. |
| Whole-toolkit adoption duplicates InfraGate policy authorities. | High | Adopt AGT selectively; keep gateway scopes, approval core, domain adapter, and execution gates authoritative. |
| Active configuration refactor causes overlapping edits. | Medium | Add only one bound guardrail section and coordinate task ordering with the appsettings-first plan. |
| New project references break container restore layers. | Medium | Observer/Planner Dockerfiles currently copy all `src/*.csproj` through the filter stage; still run Docker builds after adding adapter projects. |

## Decisions and open questions

Resolved:

- `InfraGate.AgentGuardrails` owns the one consumer-facing model-visible content seam.
- The existing gateway scanner remains an independent boundary.
- Azure is out of scope.
- AGT is a pinned deterministic adapter, not the authoritative mutation policy engine.
- A local semantic sidecar is selected by measured bakeoff, not assumption.
- Suspicious text may block model ingestion but does not automatically invalidate a legitimate deterministic
  approval workflow.

Review before implementation:

- [ ] Confirm Production semantic-sidecar outage behavior: recommended `BlockModelIngestion`.
- [ ] Confirm default quarantine retention: recommended metadata + digest only, no raw content.
- [ ] Confirm whether durable Observer/Planner outbox events are required in the first vertical slice or whether
  bounded structured logs + metrics ship first.
- [ ] Confirm whether the provider/OAuth production validator (Task F1) ships in the same branch or a coordinated
  follow-up branch after the appsettings-first refactor.

## Final verification checklist

- [ ] Every known model-ingestion path is covered: Observer snapshot, Planner anomaly, dynamic MCP tool result,
  Planner `AskObserverTool` result.
- [ ] Gateway scanner and model-visible content guard both have independent regression tests.
- [ ] AGT runtime use is offline and Azure-free.
- [ ] Semantic classifier is local, pinned, licensed, checksummed, and measured.
- [ ] Production cannot silently fail open.
- [ ] Approval-bound Kubernetes execution remains deterministic and classifier-independent.
- [ ] Metrics and logs contain no raw hostile payloads.
- [ ] `dotnet build InfraGate.slnx --configuration Release`
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] Human review completed before implementation starts.
