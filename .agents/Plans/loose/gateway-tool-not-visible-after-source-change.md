After adding `get_allowed_namespaces` to the McpGateway source, the tool was invisible to MCP clients until the Docker container was explicitly rebuilt and restarted. The gateway image is built from source at `docker compose up --build` time, so any source change requires a rebuild — but there is no guard preventing a stale container from silently serving an outdated tool list.

Worth investigating:
- Whether the gateway should expose a version or build-hash endpoint so clients (or CI) can detect stale containers
- Whether `docker compose up --build` should always recreate the container even when the image hash hasn't changed (i.e. use `--force-recreate` in dev workflows) — consider documenting this in the setup guide
- Whether the McpServer and McpGateway tool lists should be compared at gateway startup and logged as a warning when they diverge (tools registered in the gateway but missing downstream, or vice versa)
- Whether integration tests should assert the full tool list so a missing registration is caught before deployment
- claude code still doesn't see the namespaces