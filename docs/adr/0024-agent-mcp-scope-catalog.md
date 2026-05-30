# 0024. Agent MCP Scope Catalog and Read-Only Hints

Date: 2026-05-30

## Status

Accepted

## Context

The MCP Gateway previously relied on a combination of hardcoded tool lists to differentiate between read-only observability tools and potentially destructive execution tools. As new tools were added or downstream servers exposed dynamic tools, keeping these hardcoded lists in sync across the Gateway, Observer, and Planner became fragile and insecure.

Furthermore, we needed a way to restrict which tools an LLM could "see" based on its authorization scope (e.g., the Observer should not even be aware of the `propose_plan` tool), while continuing to enforce execution guards at call time.

## Decision

1. **Tool Scope Catalog as Source of Truth**: We introduce `ToolScopeCatalog` in the Gateway as the single, centralized registry of which tools require which HTTP authorization scopes. Both `ListTools` (for visibility filtering) and `CallTool` (for execution enforcement) delegate to this catalog. This ensures a tool that requires `mcp:tools.execute` is invisible to a client holding only `mcp:tools.readonly`, and any attempt to call it will also be rejected by the same logic.
2. **Read-Only Hints**: Downstream tools that are read-only are forwarded with a `ReadOnlyHint = true` annotation. The Gateway uses this hint to decide visibility. The new `IAgentMcpToolset` interface in `InfraGate.AgentMcp` unconditionally filters its `GetAgentToolsAsync` results to only tools with `ReadOnlyHint == true`, ensuring that an agent like the Observer only receives strictly read-only tools in its prompt.

## Consequences

- **Positive:** Authorization boundaries are clearer. The Observer cannot accidentally hallucinate or invoke a mutation tool because it is completely hidden from its toolset.
- **Positive:** Single source of truth. Adding a new tool with a required scope automatically handles both discovery filtering and execution enforcement.
- **Negative:** Downstream tool providers must accurately report which tools are safe (read-only) vs destructive. If a downstream provider marks a destructive tool as read-only, the Gateway will forward it with `ReadOnlyHint = true`, which could be invoked by the Observer. The Gateway must trust the downstream registry configuration.
