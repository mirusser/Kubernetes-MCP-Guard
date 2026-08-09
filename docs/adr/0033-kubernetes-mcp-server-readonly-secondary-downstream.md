# 33. kubernetes-mcp-server as a Read-Only Secondary Downstream

Date: 2026-07-12

Amended: 2026-08-09

## Status

Accepted

## Context

ADR-0021 records that `InfraGate.McpServer` may eventually be swapped for the external [`containers/kubernetes-mcp-server`](https://github.com/containers/kubernetes-mcp-server) Go binary. It now makes that possibility conditional on mutation evidence parity, explicit human approval, and a separate implementation decision. The current upstream roadmap is not evidence that the approval boundary can be replaced.

That full swap is **not** what this ADR does. It is blocked on an open question this ADR does not attempt to resolve: does `kubernetes-mcp-server`'s generic `apply` support a dry-run mode that `KubernetesPlanBuilder` can consume for its pre-approval diff? Until that's answered, replacing the primary downstream would either lose the dry-run-based approval diff or require redesigning `KubernetesPlanBuilder`'s evidence pipeline sight-unseen.

Instead, the Gateway's primary tool surface is deliberately narrow. The upstream server is useful only as a constrained diagnostic adapter for namespaced Pod and bounded log reads. Its unbounded Event listing, generic raw-resource, cluster-wide, multi-cluster, and mutation capabilities are outside this increment. Proving the AI agents benefit from the approved read surface, and proving a two-downstream Gateway architecture works, does not require weakening the mutation/approval path.

**Architecture findings that shaped the decision:**

- `IDownstreamMcpClient` was a single unkeyed DI singleton; nothing in `DownstreamToolRegistry`/`GuardedToolRunner`/`SanitizingToolCaller` had a multi-source concept.
- `DownstreamMcpClient.CreateTransportOptions()` hardcoded `Command` from a static `"dotnet"` constant and branched on .NET-specific `DownstreamAssembly`/`DownstreamProject` fields — spawning an arbitrary binary required generalizing this, not just adding a second DI registration.
- The Gateway's downstream stdio auth (the bootstrap-line protocol) is InfraGate-private; a stock Go binary can only ever run with it disabled. The Gateway's own documented security-priority order already ranks the downstream token last, below trusted launch, containment, human approval, and per-action authorization — so an always-unauthenticated-at-stdio second downstream is not a new category of risk, it's the existing lowest-priority control being absent for one process instead of weakened for both.
- `McpGatewayOptions.ValidateProductionSafety()` throws in Production mode unless `DownstreamAuth.Required == true` — that check is scoped to the primary and must never end up applying to an always-unauthenticated secondary.

## Decision

Add `kubernetes-mcp-server` as a **second, independent, read-only-only downstream MCP process**, never routed through `IDomainAdapter` / `KubernetesPlanBuilder` / `InfraGate.Approvals`.

1. **Acquisition:** `go install` the official upstream binary at a pinned tagged version (`v0.0.64` as of this writing), at build time — both `scripts/install-kubernetes-mcp-server.sh` for local dev and a dedicated `golang` build stage in `deploy/docker/mcp-gateway.Dockerfile` (`CGO_ENABLED=0` for a static binary, since the runtime image is `aspnet:10.0-noble-chiseled` with no shell/libc). Installs to `.tools/bin/kubernetes-mcp-server`, repo-local and independent of the caller's `GOPATH`.
2. **Identity and Kubernetes authorization:** the secondary receives a dedicated kubeconfig for the namespace-scoped `infra-gate-mcp-view` ServiceAccount. Its Role grants only the reads required by the approved tools, including `get` on `pods/log`, and grants neither Secret access nor mutation verbs. The primary retains its separate mutation-capable credential; descriptors never share or ambiguously select credentials.
3. **Exact Gateway policy:** the only approved secondary tools are `pods_list_in_namespace`, `pods_get`, and `pods_log`. `events_list` remains disabled because v0.0.66 provides no server-side result limit. The Gateway authorizes by immutable source identity and exact tool name, then validates namespace, allowed arguments, and reviewed bounds before dispatch. `ReadOnlyHint` remains descriptive metadata, never an authorization decision. `resources_get`, `resources_list`, cluster-wide listing, arbitrary resource kinds, and every mutation tool fail closed.
4. **Config mechanism:** a generated TOML file configures core Kubernetes only, stateless operation, and the exact approved tools. The Gateway accepts exactly `--config <path>`, rejects sibling TOML drop-ins, validates a dedicated single-context kubeconfig against the configured context, and separately enforces explicit non-wildcard namespaces. `read_only = true` is baked into the generated file. Multi-cluster discovery and default/implicit namespace selection are disabled.
5. **Trust boundary:** unauthenticated at stdio (`KubernetesMcpServerProcessOptions.AuthRequired` is a `const false`, not a default — structurally impossible to override), relying on trusted launch, credential separation, and containment (a narrower environment-variable allowlist than the primary — `PATH`/`HOME`/`TMPDIR`/`TMP`/`TEMP` only, no `InfraGate__*` variables) instead of a downstream token.
6. **Defense in depth against mutation:** read-only enforcement is independent at Kubernetes RBAC, fixed upstream configuration, Gateway source policy, and dispatcher routing. The Gateway never calls `GetDestructiveAsync()` or generates a `request_*` wrapper for the secondary, regardless of the binary's annotations. An attempted mutation against a secondary tool name fails closed without reaching the approval or execution path.
7. **Catalog and availability:** the Gateway owns one federated catalog with immutable source identity. A secondary catalog is validated as a unit for exact names, expected schemas, and collisions before it is atomically published for that child-process generation. The snapshot remains immutable until a supervised restart. The secondary is optional: startup, listing, and primary calls remain available when it is missing, rejected, unhealthy, or restarting, while health and telemetry report a bounded degraded state.
8. **Wiring shape:** `DownstreamMcpClient` remains generalized over `DownstreamProcessDescriptor`; the secondary client/registry/runner triple remains a keyed, optional registration. When disabled, the primary graph is unchanged and no secondary process is created.
9. **Evidence gate:** upstream mutation routing and removal of `InfraGate.McpServer` require a released, checksum-pinned artifact to pass the separately reviewed mutation evidence-parity contract on real Kubernetes and approval infrastructure. Any missing operation, preview/diff, freshness evidence, approval binding, audit behavior, failure semantics, or rollback proof keeps the result `no-go`.

## Consequences

- Two independent MCP subprocesses can run under one Gateway process: the primary (`InfraGate.McpServer`, full read/mutate/approve surface) and the optional secondary (`kubernetes-mcp-server`, constrained diagnostic reads only). Nothing about the mutation/approval path changes.
- Raw reads through `resources_get`, multi-cluster operation, implicit or wildcard namespaces, and upstream mutation routing are explicitly out of scope and fail closed.
- The future full swap remains a distinct, not-yet-started piece of work. Upstream roadmap statements or tool annotations are not evidence parity and cannot authorize routing changes.
- The Gateway's process/subprocess attack surface grows by one, even in read-only mode. This is judged acceptable because the new process (a) cannot reach the mutation path even if fully compromised (no code path routes it there, not just policy), and (b) sits below every documented Gateway security control except the always-last-priority downstream token, which it simply doesn't have — matching, not weakening, the existing documented priority order.
- The repo gained its first Go toolchain dependency (local dev + CI). `go install` provides Go module checksum database (GOSUMDB) verification at install time, which is weaker than the primary downstream's `DownstreamAssemblyHash` SHA-256 verification; this is noted as a follow-up hardening item if the secondary graduates past proof-of-concept, not resolved here.
- If a future released upstream version satisfies the complete evidence-parity contract and receives explicit approval, parts of this secondary-downstream wiring may be reusable. A separate plan must still define production routing, rollout, and rollback; this ADR does not authorize that change.

## References

- [ADR-0021](0021-mcpserver-local-dto-copies-over-shared-contracts.md): McpServer Uses Local DTO Copies Over a Shared Contracts Project — records the evidence-gated replacement possibility this ADR deliberately does not execute.
- `src/InfraGate.McpGateway/README.md` — "Security Controls (Priority Order)" and "Secondary downstream (kubernetes-mcp-server) trust boundary".
- `.agents/skills/infragate-mcp-gateway/SKILL.md` — "Optional secondary read-only source (off by default)".
- `docs/plans/kubernetes-mcp-server-readonly-sidecar.md` — the implementation plan this ADR records the outcome of.
- Upstream: https://github.com/containers/kubernetes-mcp-server
