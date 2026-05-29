# ADR-0022: Hidden Agent Seam vs. Native BindAsExecutor for LLM Agents

**Date:** 2026-05-29
**Status:** Accepted

---

## Context

During a code review of the `InfraGate.Planner` workflow implementation using `Microsoft.Agents.Workflows`, an architectural pattern was identified in `DecideExecutor`. The executor manually instantiates an LLM agent (`ToolCallingAgentFactory.Create()`) and manually executes it (`agent.RunAsync()`) within its `HandleAsync` method, wrapping the entire call in a custom `CancellationTokenSource`.

This creates a "Hidden Agent Seam": the LLM agent runs outside the native graph boundaries of the `Microsoft.Agents.Workflows` framework. The workflow engine's telemetry, visualization tools, and checkpointing mechanisms only see the custom `DecideExecutor` node and remain blind to the underlying LLM conversation.

The alternative approach—supported natively by the framework—would be to use `agent.BindAsExecutor()`, injecting the LLM directly into the graph as a first-class node. This would require splitting `DecideExecutor` into three distinct nodes:
1. `FormatPromptExecutor` (State preparation)
2. `AgentNode` (LLM execution)
3. `ParseDecisionExecutor` (Response parsing)

## Decision

We will **retain the "Hidden Agent Seam"** and keep the LLM execution manually wrapped inside the custom `DecideExecutor` node. We will not use the framework's native `agent.BindAsExecutor()`.

## Rationale

### 1. Per-Anomaly Wall-Clock Timeouts
The Planner must enforce strict wall-clock timeout caps (`anomalyWallClockCapSeconds`) on individual LLM responses to prevent hanging requests from blocking the pipeline. The current implementation leverages a linked `CancellationTokenSource` with `CancelAfter` specifically tailored to the LLM call inside `DecideExecutor`.

Native agent nodes bound via `BindAsExecutor()` rely on the global workflow execution token. The framework does not currently offer a native mechanism to wrap a specific edge or node with an isolated timeout, nor does it allow a seamless continuation (yielding null) when a timeout occurs without crashing the entire batch workflow.

### 2. Domain-Specific Telemetry Requirements
`DecideExecutor` is responsible for incrementing a specific Prometheus counter (`timeoutCounter`) and logging structured application events (`LogDecisionTimedOut`) when the LLM exceeds the time limit. 

Native `agent-framework` telemetry is generic. Ripping out the custom executor wrapper to use native bindings would sever our ability to natively instrument the timeout failures and parse errors with our domain-specific metrics. 

### 3. Deliberate Trade-Off Defined by Remediation Plan
The `2026-05-29-agent-framework-point1-remediation.md` plan explicitly architected `DecideExecutor` to "use `agentFactory.Create(...)` and call `agent.RunAsync(anomalyJson)`" rather than utilizing native graph binding. This proves the "seam" was an intentional trade-off to satisfy production safety and observability constraints over framework purity.

## Consequences

- **Reduced Native Observability**: We forgo the out-of-the-box `agent-framework` telemetry and visual graph representation of the agent. Tools visualizing the workflow will show `DecideExecutor` instead of an interactive agent node.
- **Improved Production Safety**: We maintain strict, domain-controlled timeouts and precise Prometheus metrics tracking for LLM responsiveness.
- **Message-Driven Integrity Maintained**: While the agent is encapsulated within the node, the workflow itself was recently refactored to pass all `AnomalyReport` payloads strictly via the framework's `HandleAsync(message)` parameter, removing the anti-pattern of capturing state in the node's constructor.
