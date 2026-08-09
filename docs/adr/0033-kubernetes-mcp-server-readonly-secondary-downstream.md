# 33. kubernetes-mcp-server as a Read-Only Secondary Downstream

Date: 2026-07-12

## Status

Accepted

## Context

ADR-0021 records the active roadmap direction: eventually swap `InfraGate.McpServer` entirely for the external [`containers/kubernetes-mcp-server`](https://github.com/containers/kubernetes-mcp-server) Go binary. That ADR is explicit that when the swap happens, `InfraGate.McpServer` is deleted in its entirety and `InfraGate.KubernetesAdapter`'s DTOs are adapted to whatever JSON schema the external server emits.

That full swap is **not** what this ADR does. It is blocked on an open question this ADR does not attempt to resolve: does `kubernetes-mcp-server`'s generic `apply` support a dry-run mode that `KubernetesPlanBuilder` can consume for its pre-approval diff? Until that's answered, replacing the primary downstream would either lose the dry-run-based approval diff or require redesigning `KubernetesPlanBuilder`'s evidence pipeline sight-unseen.

Instead, the Gateway's tool surface today is narrow — `InfraGate.McpServer` only exposes Deployment/Service/ConfigMap inspection and a fixed set of diagnostic tools (`get_k8s_status`, `get_k8s_events`, `get_pod_logs`, three `*_diagnostics` tools). `kubernetes-mcp-server` supports arbitrary resource kinds via generic `resources_list`/`resources_get`, broader Pod/event/log tools, and (per its `--toolsets` flag) other toolsets entirely (Helm, KCP, Kiali, KubeVirt, Tekton) that could matter later. Proving the AI agents benefit from that broader read surface, and proving a two-downstream Gateway architecture actually works, doesn't require resolving the dry-run question at all — as long as the second downstream never touches the mutation/approval path.

**Architecture findings that shaped the decision:**

- `IDownstreamMcpClient` was a single unkeyed DI singleton; nothing in `DownstreamToolRegistry`/`GuardedToolRunner`/`SanitizingToolCaller` had a multi-source concept.
- `DownstreamMcpClient.CreateTransportOptions()` hardcoded `Command` from a static `"dotnet"` constant and branched on .NET-specific `DownstreamAssembly`/`DownstreamProject` fields — spawning an arbitrary binary required generalizing this, not just adding a second DI registration.
- The Gateway's downstream stdio auth (the bootstrap-line protocol) is InfraGate-private; a stock Go binary can only ever run with it disabled. The Gateway's own documented security-priority order already ranks the downstream token last, below trusted launch, containment, human approval, and per-action authorization — so an always-unauthenticated-at-stdio second downstream is not a new category of risk, it's the existing lowest-priority control being absent for one process instead of weakened for both.
- `McpGatewayOptions.ValidateProductionSafety()` throws in Production mode unless `DownstreamAuth.Required == true` — that check is scoped to the primary and must never end up applying to an always-unauthenticated secondary.

## Decision

Add `kubernetes-mcp-server` as a **second, independent, read-only-only downstream MCP process**, never routed through `IDomainAdapter` / `KubernetesPlanBuilder` / `InfraGate.Approvals`.

1. **Acquisition:** `go install` the official upstream binary at a pinned tagged version (`v0.0.64` as of this writing), at build time — both `scripts/install-kubernetes-mcp-server.sh` for local dev and a dedicated `golang` build stage in `deploy/docker/mcp-gateway.Dockerfile` (`CGO_ENABLED=0` for a static binary, since the runtime image is `aspnet:10.0-noble-chiseled` with no shell/libc). Installs to `.tools/bin/kubernetes-mcp-server`, repo-local and independent of the caller's `GOPATH`.
2. **Tool scope:** a small, curated allowlist — `pods_list`, `pods_get`, `pods_log`, `events_list`, `resources_list`, `resources_get` — confirmed against the pinned binary's real `tools/list` output (all six exist verbatim, all report `readOnlyHint=true`/`destructiveHint=false`) rather than assumed from upstream docs. Defined in exactly one place, `KubernetesMcpServerProfile.EnabledTools` (`InfraGate.RunProfiles`), so nothing else hand-copies the list.
3. **Config mechanism:** a generated TOML file (`InfraGate.RunProfiles`'s first TOML output — a ~15-line hand-rolled writer, not a new dependency, since the schema needed is flat scalars plus one string array). `read_only = true` is baked in unconditionally, not configurable via any input, because nothing downstream is prepared to route mutations through this client. `kubeconfig` is derived from the same Kubernetes Domain Adapter data already used by the primary — no new YAML configuration surface was needed.
4. **Trust boundary:** unauthenticated at stdio (`KubernetesMcpServerProcessOptions.AuthRequired` is a `const false`, not a default — structurally impossible to override), relying on trusted launch (pinned-version binary path) and containment (a narrower env-var allowlist than the primary — `PATH`/`HOME`/`TMPDIR`/`TMP`/`TEMP` only, no `InfraGate__*` variables) instead of a downstream token.
5. **Defense in depth against mutation:** read-only enforcement is two independent layers — the binary's own non-configurable `read_only=true`, and the Gateway's `GatewayToolDispatcher` never calling `GetDestructiveAsync()` or generating a `request_*` wrapper for the secondary registry, regardless of what the binary's own tool annotations claim. `IsDestructiveToolAsync` reads only the primary registry; an attempted mutation against a secondary tool name falls through to the existing "unknown tool" default-deny.
6. **Wiring shape:** `DownstreamMcpClient` was generalized over a new `DownstreamProcessDescriptor` (Command/Arguments/WorkingDirectory/AuthRequired/AllowedEnvironmentVariables) with `ForPrimary(McpGatewayOptions)` and `ForKubernetesMcpServer(KubernetesMcpServerProcessOptions)` factories, rather than building an N-way multi-downstream abstraction. The secondary's `DownstreamToolRegistry`/`GuardedToolRunner`/`IDownstreamMcpClient` triple is registered via keyed DI (`McpGatewayConventions.SecondaryDownstream.ServiceKey`) and is optional — `KubernetesMcpServerProcessOptions.FromConfiguration()` returns `null` when `InfraGate:Gateway:KubernetesMcpServer:Command` is unset, and `GatewayToolDispatcher` resolves it via `IServiceProvider.GetKeyedService` (not `[FromKeyedServices]`/`GetRequiredKeyedService`), so the Gateway is unaffected wherever the secondary isn't configured.
7. **CI:** a real integration test against the actual installed binary (`GatewayKubernetesMcpServerIntegrationTests`, opt-in via the existing `INFRA_GATE_RUN_GATEWAY_INTEGRATION` gate), not a mock — running the real Gateway HTTP MCP pipeline via `TestServer`, the real generated TOML config, and the real spawned binary. Wired unconditionally (not additionally opt-in-and-skipped) into `.github/workflows/integration-tests.yml` behind a SHA-pinned `actions/setup-go`.

## Consequences

- Two independent MCP subprocesses can now run under one Gateway process: the primary (`InfraGate.McpServer`, full read/mutate/approve surface) and the secondary (`kubernetes-mcp-server`, read-only-only, broader resource-kind coverage). `tools/list` merges both read-only sets; nothing about the mutation/approval path changed.
- This does not resolve the open question blocking ADR-0021's full-swap direction (dry-run `apply` support) — that question is this ADR's own finding, not something ADR-0021 itself records. The full swap — deleting `InfraGate.McpServer` and re-pointing `KubernetesPlanBuilder` at `kubernetes-mcp-server` for mutations too — remains a distinct, larger, not-yet-started piece of work.
- The Gateway's process/subprocess attack surface grows by one, even in read-only mode. This is judged acceptable because the new process (a) cannot reach the mutation path even if fully compromised (no code path routes it there, not just policy), and (b) sits below every documented Gateway security control except the always-last-priority downstream token, which it simply doesn't have — matching, not weakening, the existing documented priority order.
- The repo gained its first Go toolchain dependency (local dev + CI). `go install` provides Go module checksum database (GOSUMDB) verification at install time, which is weaker than the primary downstream's `DownstreamAssemblyHash` SHA-256 verification; this is noted as a follow-up hardening item if the secondary graduates past proof-of-concept, not resolved here.
- If a future change makes `kubernetes-mcp-server`'s `apply` dry-run-capable and ADR-0021's full swap becomes viable, this secondary-downstream wiring (`DownstreamProcessDescriptor`, the TOML generation path, the acquisition/CI plumbing) is largely reusable — the primary work left at that point would be re-pointing the mutation/approval path, not re-solving acquisition or transport.

## References

- ADR-0021: McpServer Uses Local DTO Copies Over a Shared Contracts Project — records the longer-term full-swap direction this ADR deliberately does not execute.
- `src/InfraGate.McpGateway/README.md` — "Security Controls (Priority Order)" and "Secondary downstream (kubernetes-mcp-server) trust boundary".
- `.agents/skills/infragate-mcp-gateway/SKILL.md` — "Optional secondary read-only source (off by default)".
- `docs/plans/kubernetes-mcp-server-readonly-sidecar.md` — the implementation plan this ADR records the outcome of.
- Upstream: https://github.com/containers/kubernetes-mcp-server
