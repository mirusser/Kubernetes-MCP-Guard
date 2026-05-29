# Agent Framework Migration Roadmap

**Date:** 2026-05-29
**Focus:** Migrating `InfraGate.Observer` and `InfraGate.Planner` to leverage the [microsoft/agent-framework](https://github.com/microsoft/agent-framework).

Based on the `loose-roadmap.md` and the current state of `k8s-toolkit`, this document prioritizes the features to tackle first to modernize the AI agents. Currently, `Observer` and `Planner` are implemented using raw `Microsoft.Extensions.AI` (`IChatClient`) inside custom ASP.NET background services. This roadmap outlines the steps to replace this manual orchestration with structured, enterprise-grade abstractions.

## 1. Migrating to Managed Agent Workflows
*Maps to roadmap item: "Designing, building, and configuring AI agents, including defining roles, guardrails, prompt libraries, memory boundaries, workflows, and validation steps."*

* **Refactor the `Observer`'s `ObservationCycleLoop`:** Replace the manual LLM calling loop and strict `(max 8)` tool-call cap with an `Agent` abstraction from the framework. Define an explicit agent graph where the `Observer` agent natively handles its own tool iteration.
* **Refactor the `Planner`'s `BatchProcessor`:** Use the framework's sequential workflow capabilities to manage the lifecycle of receiving a batch of anomalies, analyzing them, and emitting `propose_plan` decisions. 
* **Standardize Memory Boundaries:** The framework provides native chat history management. Offload the manual tracking of context windows and snapshot inclusions to the framework's managed state and chat history objects.

## 2. Standardizing the Prompt Libraries
*Maps to roadmap item: "Developing and maintaining structured prompt libraries for AI agents supporting tasks across the SDLC."*

* **Transition Static Prompts:** Move away from relying entirely on static markdown files (e.g., `Prompts/ObserverSystemPrompt.md`).
* **Adopt Framework Templates:** Transition these into the framework's structured prompt templates (similar to Semantic Kernel's semantic functions). This treats prompts as configuration assets that can take templated arguments (like `SnapshotDocument` or `AnomalyHandoffBatch`), making continuous tuning and versioning much easier.

## 3. Integrating MCP Tools natively into the Framework
*Maps to roadmap item: "Integrating AI agents with enterprise tools..."*

* **Replace Custom Calling Logic:** The agents currently maintain an explicit tool whitelist and serialize tool calls manually. We will replace this with the Microsoft Agent Framework's deep extensibility for plugins.
* **Build MCP Tool Providers:** Build an MCP Plugin/Tool-Provider for the agent framework that dynamically loads the `mcp:tools.readonly` scope for the `Observer` and the `mcp:tools.propose` scope for the `Planner`. The framework's LLM router will seamlessly decide when to invoke the MCP gateway without custom loop logic.

## 4. Upgrading AI Observability and Telemetry
*Maps to roadmap item: "Implementing AI observability practices, including usage tracking, cost monitoring, and output quality evaluation."*

* **Adopt OpenTelemetry:** The current setup relies on manual `System.Diagnostics.Metrics`. The Microsoft Agent Framework is built with OpenTelemetry from the ground up.
* **Integrate with Serilog Stack:** Hook the framework's native token usage tracking, trace graphs, and latency metrics directly into the existing `InfraGate.Observability` (Serilog) stack. This will provide deep, span-level visibility into exactly what the LLM is doing during an observation cycle.

## 5. Enforcing Framework-Level Guardrails
*Maps to roadmap items: "Defining and maintaining guardrails to ensure reliable, secure, and compliant AI behavior." / "Establishing and monitoring controls for hallucinations..."*

* **Implement Interceptors:** While `InfraGate.McpGateway` acts as the ultimate runtime guardrail, we will implement the framework's *Interceptors* or *Filters* at the client level.
* **Pre-execution Validation:** Before the `Planner` calls `propose_plan`, use the framework to run a lightweight, deterministic validation step (or a secondary lightweight LLM judge) to ensure the plan matches the `restart_deployment` / `scale_deployment` whitelist. This prevents wasted gateway calls and helps track hallucination rates in the metrics.
