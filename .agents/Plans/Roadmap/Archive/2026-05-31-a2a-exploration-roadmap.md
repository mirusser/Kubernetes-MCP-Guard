# Implementation Plan: A2A Protocol Exploration Roadmap

## Overview
This roadmap outlines the systematic adoption of advanced Agent-to-Agent (A2A) capabilities in the InfraGate (`k8s-toolkit`) repository. It moves the system from a simple fire-and-forget handoff (dirty plumbing) to a rich, multi-agent conversational architecture featuring streaming feedback, capability negotiation, tool delegation, and a stateful Executor agent.

## Architecture Decisions
- **Incremental Adoption:** We will introduce A2A features one at a time, ensuring each phase leaves the system in a stable, working state.
- **Unified Tracing (Streaming):** We will use A2A streaming (`AgentEventQueue`) to push Planner execution state back to the Observer, centralizing the anomaly lifecycle trace in the Observer's Audit Outbox.
- **Stateful Executor Agent:** The `InfraGate.Executor` will be transitioned from a deterministic polling watcher to an explicit A2A Agent. This moves deduplication and plan-state management to the Executor, simplifying the Planner.
- **Bi-directional Tooling:** We will implement cross-agent tool execution via standard `AgentMessage` tool-call payloads rather than direct HTTP side-channels.

## Task List

### Phase 1: Foundation - Streaming Execution Feedback
**Description:** Upgrade the fire-and-forget A2A link to a streaming connection so the Observer receives real-time progress from the Planner.

- [ ] **Task 1: Update Planner to yield status chunks**
  - **Description:** Modify `PlannerHandoffAgentHandler` to emit `AgentMessage` chunks via `queue.WriteAsync()` as the batch moves through the `BatchProcessor` pipeline.
  - **Acceptance criteria:**
    - [ ] Planner yields "Received", "Analyzing", and "Plan Proposed" events to the `AgentEventQueue`.
  - **Files likely touched:** `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs`, `src/InfraGate.Planner/Pipeline/BatchProcessor.cs`
  - **Verification:** `dotnet test tests/InfraGate.Planner.Tests/`
  - **Estimated scope:** Small

- [ ] **Task 2: Update Observer to consume stream**
  - **Description:** Update `A2AAnomalyHandoffSink` to iterate over `A2AClient.RunAsync()` chunks and append them to the local Audit Outbox before completing.
  - **Acceptance criteria:**
    - [ ] Observer does not disconnect immediately; it streams and logs Planner updates.
  - **Files likely touched:** `src/InfraGate.Observer/Handoff/A2AAnomalyHandoffSink.cs`
  - **Verification:** `dotnet test tests/InfraGate.Observer.Tests/`
  - **Estimated scope:** Small

### Checkpoint: Foundation
- [ ] Tests pass, builds clean.
- [ ] Observer logs now show real-time Planner progression.

### Phase 2: Pre-Handoff Capability Negotiation
**Description:** The Observer will ask the Planner if it can handle an anomaly kind before sending the full payload.

- [ ] **Task 3: Implement Capability Handshake in Planner**
  - **Description:** Update Planner handler to respond to a "CapabilityCheck" intent with its supported remediation kinds (e.g., `Deployment`, `StatefulSet`).
  - **Acceptance criteria:**
    - [ ] Planner responds with an A2A message containing a list of supported operations.
  - **Estimated scope:** Small

- [ ] **Task 4: Implement Pre-Flight Check in Observer**
  - **Description:** The Observer sends a capability check for discovered anomalies. If the Planner rejects the kind, Observer marks it as `Unremediable` and drops it.
  - **Acceptance criteria:**
    - [ ] Unsupported anomalies bypass the handoff queue entirely.
  - **Estimated scope:** Medium

### Checkpoint: Capability Negotiation
- [ ] E2E tests confirm unsupported anomalies are dropped at the Observer.

### Phase 3: Context Requests (Bi-directional Tool Delegation)
**Description:** Allow the Planner to request additional context (like Pod logs) from the Read-Only Observer.

- [ ] **Task 5: Enable Observer to handle Tool Requests**
  - **Description:** Register an A2A Server/Handler in the Observer specifically for executing Read-Only Kubernetes tools on behalf of the Planner.
  - **Estimated scope:** Medium

- [ ] **Task 6: Update Planner LLM prompts for reverse-delegation**
  - **Description:** Provide the Planner with an A2A tool to query the Observer if confidence in a remediation plan is low due to missing context.
  - **Estimated scope:** Medium

### Checkpoint: Context Requests
- [ ] Planner can successfully request logs from Observer via A2A before proposing a plan.

### Phase 4: Transform Executor into an A2A Agent
**Description:** Re-architect the Executor from a passive watcher to an active A2A Agent.

- [ ] **Task 7: Scaffold Executor A2A Server**
  - **Description:** Replace Executor's HTTP endpoints with `AddA2AServer("executor")` and map an `ExecutorAgentHandler`.
  - **Estimated scope:** Medium

- [ ] **Task 8: Implement A2A Proposal Handoff**
  - **Description:** Update Planner's `RemediationProposalSink` to use `A2AClient` targeting the Executor.
  - **Estimated scope:** Small

- [ ] **Task 9: Add Executor Pre-Flight State Check**
  - **Description:** Implement an additional layer of security where the Planner queries the Executor via A2A to verify if a plan is already pending approval. The `DedupeGateExecutor` remains in the Planner as the first line of defense; the Executor state-check acts as a secondary, definitive guard before finalizing a proposal.
  - **Estimated scope:** Large

### Checkpoint: Complete Roadmap
- [ ] All acceptance criteria met.
- [ ] `docs/observer-planner-flow.md` updated.
- [ ] Full E2E tests pass for the entire A2A triad (Observer <-> Planner <-> Executor).

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Circular A2A loops (Observer calls Planner calls Observer) | High | Implement strict `CorrelationId` and hop-count limits in A2A headers. |
| Streaming timeouts | Medium | Adjust `HttpClient` timeout for `A2AClient` during long-running LLM planning phases. |
| Executor state loss | Medium | Ensure Executor persists pending plans to Postgres *before* acknowledging the A2A message from the Planner. |

## Open Questions
- Do we want the Executor to proactively push "Approval Granted" / "Plan Executed" A2A events back up the chain to the Planner, or should the Planner be stateless regarding execution?
