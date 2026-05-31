# Implementation Plan: Migrate Observer-to-Planner Handoff to A2A Protocol

## Overview
We are migrating the handoff communication between `InfraGate.Observer` and `InfraGate.Planner` from a bespoke HTTP POST endpoint (`/handoff/anomalies`) to the native Agent-to-Agent (A2A) protocol provided by the Microsoft Agent Framework. This standardizes the communication, leverages the framework's transport layer, and removes custom HTTP client and endpoint boilerplate.

Two design choices (decided after a feasibility pass against official docs + source) shape this plan:
- **Receiver = a custom `IAgentHandler`, not a subclassed `AIAgent`.** The Planner does not want LLM/agent-run semantics — it just deserializes, audits, and enqueues. Registering a keyed `IAgentHandler` *replaces the default `A2AAgentHandler` entirely* and is the documented "full control of request processing" hook. No `AIAgent` registration is required.
- **Sender = direct `A2AClient` configuration, not `A2ACardResolver`.** We construct `new A2AClient(plannerUri, authedHttpClient).AsAIAgent(...)` with the Observer's existing client-credentials `HttpClient`. This guarantees the bearer token is attached to the actual message call (not just card discovery) and removes the need to serve an agent card / `.well-known` endpoint on the Planner.

## Architecture Decisions
- **Planner as A2A Host (custom handler)**: `InfraGate.Planner` registers a keyed `IAgentHandler` (`PlannerHandoffAgentHandler`) under the agent name `planner-agent`, calls `builder.AddA2AServer("planner-agent")`, and maps `app.MapA2AHttpJson("planner-agent", "/a2a/planner")`. The HTTP+JSON binding is the A2A v1 default, matching the direct client below.
- **Observer as A2A Client (direct config)**: `InfraGate.Observer` constructs `A2AClient` directly against the Planner's `/a2a/planner` endpoint, passing the `PlannerHandoff` named `HttpClient` (which already has `AddClientCredentialsBearerHandler()`), then calls `.AsAIAgent(...)` and `RunAsync()`. No `A2ACardResolver`, no agent-card discovery, no `.well-known` path.
- **Serialization**: We continue serializing `AnomalyHandoffBatch` as a JSON string inside the A2A message payload, so the existing queue-processing logic is reused verbatim. `RunAsync(jsonString)` becomes a user `ChatMessage`; the handler reads `context.Message.ToChatMessage().Text` and deserializes.
- **Auditing**: The `HandoffReceived` audit event moves from the removed minimal API endpoint into `PlannerHandoffAgentHandler` (still emitted via `IPlannerAuditOutbox`).
- **Observability/audit parity (Observer side)**: The new sink must preserve the current `HttpAnomalyHandoffSink` behavior — `HandoffPublished` / `HandoffFailed` audit events and the failed/backpressure metric counters — adapted to A2A's exception-based failure model (a returned response = published; a thrown exception = failed). See Risks for the backpressure nuance.
- **Packages are preview (accepted)**: The A2A packages are preview-only — there is no stable release. We accept this and **pin** `1.8.0-preview.260528.1` for reproducibility. Their dependencies align with the repo: they require `Microsoft.Agents.AI.Abstractions 1.8.0` (stable — what `Microsoft.Agents.AI 1.8.0` already resolves) and `Microsoft.Extensions.AI 10.5.1` (repo has 10.6.0 ≥ that).

## Task List

### Phase 1: Foundation (A2A Setup & Receiver)
- [ ] **Task 1: Add A2A NuGet Packages**
  - **Description**: Add the A2A packages to both projects, pinned to the preview version.
  - **Acceptance criteria**:
    - [ ] `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` version `1.8.0-preview.260528.1` added to `InfraGate.Planner` (transitively brings `Microsoft.Agents.AI.Hosting.A2A`, `A2A`, `A2A.AspNetCore`).
    - [ ] `Microsoft.Agents.AI.A2A` version `1.8.0-preview.260528.1` added to `InfraGate.Observer` (brings the `A2A` SDK + `AsAIAgent` extensions). Optionally add an explicit `A2A` `1.0.0-preview2` reference for clarity since the Observer uses `A2AClient` directly.
    - [ ] No `A2A.AspNetCore` / `MapWellKnownAgentCard` needed on the Planner — direct client config makes the agent card unnecessary.
  - **Verification**:
    - [ ] `dotnet restore` succeeds (prerelease resolution).
    - [ ] No NuGet downgrade warnings for `Microsoft.Agents.AI.Abstractions` (must stay 1.8.0) or `Microsoft.Extensions.AI` (stays 10.6.0).
  - **Dependencies**: None
  - **Files likely touched**:
    - `src/InfraGate.Planner/InfraGate.Planner.csproj`
    - `src/InfraGate.Observer/InfraGate.Observer.csproj`
  - **Estimated scope**: XS

- [ ] **Task 2: Implement `PlannerHandoffAgentHandler` (custom `IAgentHandler`)**
  - **Description**: Create a custom A2A handler in the Planner that takes full control of request processing: read the incoming A2A message text, deserialize it to `AnomalyHandoffBatch`, emit the `HandoffReceived` audit event, enqueue to `AnomalyBatchQueue`, and push a terminal ack event so the caller's `RunAsync` completes.
  - **Acceptance criteria**:
    - [ ] `PlannerHandoffAgentHandler` implements `IAgentHandler` (`ExecuteAsync(RequestContext, AgentEventQueue, CancellationToken)` and `CancelAsync(...)`).
    - [ ] `ExecuteAsync` reads the batch JSON from `context.Message` (e.g. `context.Message?.ToChatMessage().Text`) and deserializes to `AnomalyHandoffBatch`.
    - [ ] `HandoffReceived` is emitted via `IPlannerAuditOutbox` with the existing payload shape (`cycleId`, `anomalyIds`, `count`, `ActorSubject: "service:observer"`, `Outcome: "received"`).
    - [ ] The batch is enqueued via `AnomalyBatchQueue.TryEnqueue`. If `TryEnqueue` returns false (backpressure), surface an A2A failure/error rather than silently dropping (enables the Observer to record backpressure — see Task 4).
    - [ ] A terminal message/completion event is enqueued onto `AgentEventQueue` so the A2A turn completes (the Observer's `RunAsync` returns instead of hanging). `CancelAsync` is a no-op.
  - **Verification**:
    - [ ] Code compiles without errors.
    - [ ] Unit test: handler deserializes a sample batch, asserts audit emission + `TryEnqueue` call + terminal event.
  - **Dependencies**: Task 1
  - **Files likely touched**:
    - `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs`
  - **Estimated scope**: S

- [ ] **Task 3: Expose Planner A2A Server & Remove HTTP Endpoint**
  - **Description**: Register the custom handler + A2A server in the Planner's `Program.cs`, map the A2A endpoint behind the existing authorization policy, and delete the legacy `HandoffEndpoint.cs`.
  - **Acceptance criteria**:
    - [ ] `builder.Services.AddKeyedSingleton<IAgentHandler>("planner-agent", ...)` registers `PlannerHandoffAgentHandler` (resolving `AnomalyBatchQueue` + `IPlannerAuditOutbox` from DI).
    - [ ] `builder.AddA2AServer("planner-agent")` is registered.
    - [ ] `app.MapA2AHttpJson("planner-agent", "/a2a/planner").RequireAuthorization(PlannerConventions.Policies.ObserverSender)` is configured. (`MapA2AHttpJson` returns `IEndpointConventionBuilder`, so the existing `ObserverSender`/`azp == infra-gate-observer` policy chains on exactly as the old `MapPost` did — confirmed in source.)
    - [ ] Existing JwtBearer authentication (`ValidateAudience = false`) and `UseAuthentication`/`UseAuthorization` ordering are preserved.
    - [ ] `HandoffEndpoint.cs` is deleted and `app.MapPlannerHandoffEndpoint()` removed.
  - **Verification**:
    - [ ] Planner project builds successfully.
  - **Dependencies**: Task 2
  - **Files likely touched**:
    - `src/InfraGate.Planner/Program.cs`
    - `src/InfraGate.Planner/Endpoints/HandoffEndpoint.cs` (Deleted)
  - **Estimated scope**: S

### Checkpoint: Foundation
- [ ] Tests pass, Planner builds clean
- [ ] Planner starts up locally without crashing; `/a2a/planner` is reachable and returns 401/403 without the Observer's bearer token

### Phase 2: Core Features (Sender Migration)
- [ ] **Task 4: Implement `A2AAnomalyHandoffSink` (direct `A2AClient`)**
  - **Description**: Create the new handoff sink in the Observer that builds an `A2AClient` directly against the Planner endpoint using the authenticated `HttpClient`, wraps it via `AsAIAgent`, and invokes `RunAsync` with the serialized batch — while preserving the current sink's audit + metric behavior.
  - **Acceptance criteria**:
    - [ ] `A2AAnomalyHandoffSink` implements `IAnomalyHandoffSink`.
    - [ ] Constructs `new A2AClient(new Uri(plannerA2AUrl), authedHttpClient).AsAIAgent(name: ..., description: ...)`, where `authedHttpClient` is the `PlannerHandoff` named client (with `AddClientCredentialsBearerHandler()`). Both `A2AClient` and `A2ACardResolver` accept an `HttpClient` — confirmed in source — so client-credentials bearer auth is preserved on the actual call.
    - [ ] Serializes `AnomalyHandoffBatch` to JSON and sends it via `agent.RunAsync(json, cancellationToken)`.
    - [ ] Skips empty batches (parity with current sink).
    - [ ] Audit/metric parity: a returned response emits `HandoffPublished`; a thrown exception emits `HandoffFailed` and increments the failed counter. If the Planner signals overload (Task 2 backpressure error), map it to the backpressure counter; otherwise document that the 429/backpressure path is dormant (the Planner does not currently emit it).
  - **Verification**:
    - [ ] Code compiles without errors.
    - [ ] Unit test: success path emits `HandoffPublished`; a thrown `RunAsync` emits `HandoffFailed` + counter.
  - **Dependencies**: Task 1
  - **Files likely touched**:
    - `src/InfraGate.Observer/Handoff/A2AAnomalyHandoffSink.cs`
  - **Estimated scope**: S

- [ ] **Task 5: Update Observer Wire-up & Remove Legacy Sink**
  - **Description**: Update the Observer's DI to compose `A2AAnomalyHandoffSink` (gated on the Planner A2A URL) into the existing `CompositeAnomalyHandoffSink` where `HttpAnomalyHandoffSink` used to sit, and delete the legacy sink.
  - **Acceptance criteria**:
    - [ ] `Program.cs` builds `A2AAnomalyHandoffSink` with the `PlannerHandoff` named `HttpClient` and adds it to the composite (replacing the `HttpAnomalyHandoffSink` branch).
    - [ ] `HttpAnomalyHandoffSink.cs` is deleted.
    - [ ] `LoggingAnomalyHandoffSink` and `JsonFileAnomalyHandoffSink` branches are unchanged.
  - **Verification**:
    - [ ] Observer project builds successfully.
  - **Dependencies**: Task 4
  - **Files likely touched**:
    - `src/InfraGate.Observer/Program.cs`
    - `src/InfraGate.Observer/Handoff/HttpAnomalyHandoffSink.cs` (Deleted)
  - **Estimated scope**: S

- [ ] **Task 6: Update Tests, Conventions & Config**
  - **Description**: Clean up everything that referenced the old endpoint/sink and the route/env config.
  - **Acceptance criteria**:
    - [ ] Remove/replace `PlannerConventions.HandoffAnomaliesEndpointPath` usages; keep or rename the route constant to `/a2a/planner` as appropriate.
    - [ ] Update Planner integration tests that POST to `/handoff/anomalies` to exercise the A2A endpoint (or assert auth at it).
    - [ ] Update/replace Observer `HttpAnomalyHandoffSink` tests with `A2AAnomalyHandoffSink` tests.
    - [ ] Update the Observer's `PlannerHandoffUrl` value (env var + `docker-compose` + run profiles) from the `/handoff/anomalies` URL to the `/a2a/planner` base URL.
  - **Verification**:
    - [ ] `dotnet test` passes for affected suites.
  - **Dependencies**: Tasks 3, 5
  - **Estimated scope**: S

### Checkpoint: Core Features
- [ ] Both Observer and Planner build cleanly.
- [ ] E2E or unit tests involving the handoff logic pass (updated as necessary).
- [ ] (Manual check) Run both services locally and verify anomalies are transmitted over A2A and that the Observer's bearer token is enforced by the `ObserverSender` policy at `/a2a/planner`.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| A2A packages are **preview-only** (no stable release) on a production path | Med | Accepted. Pin `1.8.0-preview.260528.1`; re-pin deliberately on upgrade; watch for the first stable A2A release. |
| Package version mismatch with existing `Microsoft.Agents.*` libraries | **Low** (reassessed from High) | Versions align: A2A pkgs need `Microsoft.Agents.AI.Abstractions 1.8.0` (already resolved by `Microsoft.Agents.AI 1.8.0`) and `Microsoft.Extensions.AI 10.5.1` (repo has 10.6.0). Verify no downgrade warnings on restore. |
| Loss of auth/identity context | Low | Server: `MapA2AHttpJson(...).RequireAuthorization(ObserverSender)` chains (returns `IEndpointConventionBuilder`). Client: direct `A2AClient(uri, authedHttpClient)` attaches the client-credentials bearer to the real call. Both confirmed in source. |
| A2A turn never completes (handler doesn't enqueue a terminal event) → Observer `RunAsync` hangs | Med | Task 2 explicitly enqueues a terminal message/completion event onto `AgentEventQueue`; cover with a unit/integration test. |
| Observability gap — backpressure (429) has no direct A2A analog | Low | The Planner endpoint never actually emits 429 today (it always `Accepted` + `TryEnqueue`), so the path is already dormant. Optionally upgrade: handler surfaces an A2A error when `TryEnqueue` returns false and the sink maps it to the backpressure counter. |
| A2A default session/task stores are `InMemory` ("development only") | Low | Irrelevant for fire-and-forget enqueue (no session continuity needed); background responses are `DisallowBackground` so the handoff stays a synchronous ack. |
| Route/config drift | Low | Task 6 updates env vars, `docker-compose`, and run profiles from `/handoff/anomalies` to `/a2a/planner`. |

## Open Questions
- None currently.

**Resolved Questions:**
- **Receiver shape:** custom `IAgentHandler` (`PlannerHandoffAgentHandler`) registered keyed by `"planner-agent"`, replacing the default `A2AAgentHandler`. No `AIAgent` registration needed.
- **Sender shape:** direct `A2AClient` configuration (`new A2AClient(plannerUri, authedHttpClient).AsAIAgent(...)`) — **not** `A2ACardResolver`. Avoids serving an agent card and keeps auth on the message call.
- **Endpoint mapping:** new convention `/a2a/planner` (HTTP+JSON binding) instead of the legacy `/handoff/anomalies`.
- **Authorization integration:** `MapA2AHttpJson` returns `IEndpointConventionBuilder` (verified in `A2AEndpointRouteBuilderExtensions.cs`), so the existing policy chains directly:
  ```csharp
  app.MapA2AHttpJson("planner-agent", "/a2a/planner")
     .RequireAuthorization(PlannerConventions.Policies.ObserverSender);
  ```
- **Packages/versions:** preview accepted; pin `1.8.0-preview.260528.1`. Planner: `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`. Observer: `Microsoft.Agents.AI.A2A` (+ optional explicit `A2A`).

## Verification Notes (sources)
- A2A hosting (`AddA2AServer`, `MapA2AHttpJson`, custom `IAgentHandler`, in-memory store warning): https://learn.microsoft.com/agent-framework/hosting/agent-to-agent
- A2A client (direct config, `A2AClient.AsAIAgent`, `A2AClientOptions`): https://learn.microsoft.com/agent-framework/agents/providers/agent-to-agent
- `MapA2AHttpJson`/`MapA2AJsonRpc` return `IEndpointConventionBuilder`: `microsoft/agent-framework` → `dotnet/src/Microsoft.Agents.AI.Hosting.A2A.AspNetCore/A2AEndpointRouteBuilderExtensions.cs`
- `IAgentHandler.ExecuteAsync(RequestContext, AgentEventQueue, CancellationToken)` + default `context.Message.ToChatMessage()` / `RunAsync` bridge: `microsoft/agent-framework` → `dotnet/src/Microsoft.Agents.AI.Hosting.A2A/A2AAgentHandler.cs`
- `A2AClient(Uri, HttpClient?)` and `A2ACardResolver(Uri, HttpClient?, ...)` accept a custom `HttpClient`: `a2aproject/a2a-dotnet` → `src/A2A/Client/A2AClient.cs`, `src/A2A/Client/A2ACardResolver.cs`
- Package versions + dependency nuspecs (`Abstractions 1.8.0`, `Microsoft.Extensions.AI 10.5.1`, `A2A 1.0.0-preview2`): NuGet flat-container indexes for `microsoft.agents.ai.hosting.a2a.aspnetcore` and `microsoft.agents.ai.a2a`
