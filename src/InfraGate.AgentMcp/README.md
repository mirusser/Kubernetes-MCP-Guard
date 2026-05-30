# InfraGate.AgentMcp

`InfraGate.AgentMcp` provides the `IAgentMcpToolset` abstraction to connect agents (Observer, Planner) to the MCP Gateway. It filters tools based on a `ReadOnlyHint` and serves as a secure boundary so agents only see what they are authorized to invoke.

**Owns:** MCP client connection lifecycle, `IAgentMcpToolset` abstraction, tool filtering by `ReadOnlyHint`.

## Contents

- `IAgentMcpToolset.cs` defines the contract for fetching tools and calling them.
- `AgentMcpToolset.cs` provides the concrete implementation wrapping an `McpClient` with `ReadOnlyHint` filtering.
- `AgentMcpOptions.cs` contains the connection and filtering options.

## Boundaries

This project references `ModelContextProtocol.NET.Client` to interact with the Gateway. It is consumed by `InfraGate.Observer` and `InfraGate.Planner`.
