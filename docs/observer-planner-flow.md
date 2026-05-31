# Observer and Planner Handoff Flow

This document visualizes the detection, processing, and handoff flow between the autonomous `InfraGate.Observer` service and the analytical `InfraGate.Planner` service.

This document describes the **current** bidirectional Agent-to-Agent (A2A) channel architecture.

## Channel Overview

The Observer and Planner communicate over two A2A channels:

1. **Observer → Planner (handoff)**: the Observer POSTs an `AnomalyHandoffBatch` to the Planner's A2A endpoint (`/a2a/planner`). This is the original direction and is unchanged.
2. **Planner → Observer (progress + questions)**: the Planner calls back to the Observer's A2A endpoint (`/a2a/observer`) to deliver **Plan Progress Notifications** and to ask **Reverse Context Requests** (read-only K8s tool calls via `ask_observer_to_inspect`). The Observer is the A2A server; the Planner is the A2A client.

## Object Flow

```mermaid
flowchart TD
    k8s[Kubernetes Cluster]
    snapshot[Snapshot Document]
    obsAgent[Observer Agent LLM]
    rawAnomaly[Raw LlmAnomalyOutput]
    parsedAnomaly[Anomaly Report]
    handoffBatch[Anomaly Handoff Batch]
    queue[Planner Batch Queue]
    planAgent[Planner Agent LLM]
    proposal[Remediation Proposal]

    k8s -->|MCP Tools| snapshot
    snapshot --> obsAgent
    obsAgent --> rawAnomaly
    rawAnomaly -->|Severity & Parse| parsedAnomaly
    parsedAnomaly -->|Dedupe Store| handoffBatch
    handoffBatch -->|A2A Protocol| queue
    queue -->|Filter & Dedupe| planAgent
    planAgent -->|Propose Tool| proposal
    
    parsedAnomaly -.->|Audit| auditObs[Observer Audit Outbox]
    handoffBatch -.->|Audit| auditPlan[Planner Audit Outbox]

    planAgent -->|A2A: progress| obsInbound[Observer Inbound Handler]
    planAgent -->|A2A: tool-request| obsInbound
    obsInbound -.->|Audit| auditObs
```

## Ownership

```mermaid
flowchart LR
    subgraph observer[InfraGate.Observer]
        obsCycle[Cycle Loop]
        obsSnap[Snapshot Fetcher]
        obsClassify[Severity Classifier]
        obsDedupe[Observer Dedupe Store]
        obsSink[Handoff Sink]
    end

    subgraph gateway[InfraGate.McpGateway]
        mcpTools[Read-Only Tools]
        mcpPropose[Propose Tool]
    end

    subgraph planner[InfraGate.Planner]
        planQueue[Anomaly Batch Queue]
        planBatch[Batch Processor]
        planDedupe[Planner Dedupe Gate]
        planAgent[Decide Executor]
        planSink[Proposal Sink]
    end

    obsCycle --> obsSnap
    obsSnap --> mcpTools
    obsClassify --> obsDedupe
    obsDedupe --> obsSink
    
    obsSink -->|A2A Protocol| planQueue
    
    planQueue --> planBatch
    planBatch --> planDedupe
    planDedupe --> planAgent
    planAgent --> mcpPropose
    planAgent --> planSink
```

The **Observer** owns temporal scheduling, raw state extraction (snapshots), initial anomaly hallucination filtering, severity classification, and cross-cycle deduplication.
The **Planner** owns queue management, remediation strategy generation, safety validation (preventing conflicting plans), and formal plan proposal to the MCP server.

## Component Workflow Graphs (Microsoft.Agents.AI.Workflows)

Both services use the `Microsoft.Agents.AI.Workflows` engine to process items as a Directed Acyclic Graph (DAG).

### Observer DAG
```mermaid
flowchart TD
    input[Cycle Input]
    snap[Snapshot Executor]
    agent[Observer Agent Executor]
    parse[Anomaly Parse Executor]
    agg[Cycle Aggregate Executor]

    input --> snap
    snap --> agent
    agent --> parse
    parse --> agg
```
*(Note: The Observer fans out the Snapshot, Agent, and Parse executors per Kubernetes namespace before fanning back in at the Aggregate node).*

### Planner DAG
```mermaid
flowchart TD
    intake[Batch Intake]
    filter[Filter Executor]
    dedupe[Dedupe Gate Executor]
    decide[Decide Executor]
    validate[Validate Executor]
    propose[Propose Executor]

    intake --> filter
    filter --> dedupe
    dedupe --> decide
    decide --> validate
    validate --> propose
```
*(Note: The Planner fans out from Intake through Propose per anomaly in the received batch).*

## Sequence Diagram

```mermaid
sequenceDiagram
    participant K8s as Kubernetes
    participant Obs as InfraGate.Observer
    participant Gateway as MCP Gateway
    participant Plan as InfraGate.Planner
    participant Exec as InfraGate.Executor

    loop Every CycleIntervalSeconds
        Obs->>Gateway: get_k8s_status, events, pods, etc.
        Gateway->>K8s: Query APIs
        Gateway-->>Obs: Raw text responses
        Obs->>Obs: Build SnapshotDocument
        Obs->>Obs: LLM evaluates Snapshot
        Obs->>Obs: Parse, Classify Severity, Dedupe
        Obs->>Obs: Log to Observer Audit Outbox
        opt If anomalies are active/resolved
            Obs->>Plan: A2A Protocol /a2a/planner (JWT Bearer)
        end
    end

    Plan->>Plan: Log HandoffReceived to Planner Audit
    Plan->>Plan: Enqueue AnomalyHandoffBatch
    Plan-->>Obs: 202 Accepted
    
    loop Background Batch Processor
        Plan->>Plan: Dequeue Batch
        Plan->>Obs: A2A /a2a/observer — progress: Analyzing
        Obs->>Obs: Log handoff.progress to Audit Outbox
        Plan->>Plan: Filter out resolved anomalies
        Plan->>Plan: Dedupe Gate (check for active plans)
        Plan->>Gateway: LLM evaluates anomaly + context tools
        opt LLM calls ask_observer_to_inspect
            Plan->>Obs: A2A /a2a/observer — tool-request
            Obs->>Gateway: Read-only MCP tool (whitelist-gated)
            Obs-->>Plan: ToolResponsePayload
            Obs->>Obs: Log handoff.tool_served / tool_denied
        end
        Plan->>Plan: Validate remediation strategy
        Plan->>Gateway: Call propose_plan MCP tool
        Plan->>Plan: Log to Planner Audit Outbox
        Plan->>Exec: HTTP POST RemediationProposalBatch
        Plan->>Obs: A2A /a2a/observer — progress: PlanProposed | NoAction
        opt Processing exception
            Plan->>Obs: A2A /a2a/observer — progress: Failed
        end
    end
```

## Scenarios To Verify

### Happy Path
1. Observer loop fires.
2. Snapshot is fetched and fed to the Observer LLM.
3. LLM identifies a failing Pod.
4. Observer parses it into an `AnomalyReport`, classifies its severity, and verifies it isn't suppressed in the `AnomalyDedupeStore`.
5. Observer POSTs the report in a batch to Planner.
6. Planner queues the batch.
7. Planner dequeues, passes the Filter and Dedupe gates.
8. Planner LLM investigates the specific Pod and devises a rollout restart strategy.
9. Planner validates the strategy and formally proposes it to the Gateway.
10. Planner forwards the `RemediationProposal` to the Executor.

### Deduped / Suppressed Anomaly
1. Observer detects a failing Pod.
2. Observer checks the `AnomalyDedupeStore`.
3. The store sees this exact anomaly was reported 1 minute ago, which is within the `DedupeSuppressionWindow`.
4. The anomaly is suppressed.
5. The suppressed report is logged to the Observer Audit Outbox.
6. No A2A handoff message is sent to the Planner.

### Resolved Anomaly
1. Observer detects zero anomalies.
2. The `AnomalyDedupeStore` notices a previously active anomaly has not been seen for longer than the `DedupeResolutionThreshold`.
3. The store produces a "Resolved" `AnomalyReport`.
4. Observer POSTs the "Resolved" batch to the Planner.
5. Planner queues and dequeues the batch.
6. Planner's `FilterExecutor` sees the "Resolved" status and terminates the workflow for that item immediately. No LLM call is made.

### Existing Plan Dedupe (Planner Side)
1. Planner receives an active anomaly.
2. Planner's `DedupeGateExecutor` checks the `PlannerDedupeStore`.
3. The store indicates there is already an active or pending remediation plan proposed for this exact anomaly target in a previous cycle.
4. The workflow for that anomaly terminates early to prevent the LLM from proposing duplicate, overlapping plans.
