# ADR-0017: Autonomous Remediation Uses a Two-Process Planner + Executor Split

**Date:** 2026-05-24
**Status:** Accepted

---

## Context

The Anomaly Observer (ADR-0012 through ADR-0015) closed the read side of the autonomous loop: it emits `AnomalyReport`s through `IAnomalyHandoffSink` for a downstream consumer to act on. The next step is the action side — turning an `AnomalyReport` into an approved-and-executed **Mutation Intent** through the existing approval profile.

That work splits into two responsibilities:

- Reason about a candidate remediation, choose a mutation operation + arguments, and create a **Plan Envelope** via a new gateway tool.
- Wait for the **Approval Grant** and call `execute_approved_plan` once the grant is issued.

Three topologies were considered:

- **(i) One process, two internal roles.** A single `InfraGate.Remediation` project containing both responsibilities, one Keycloak client carrying both scopes, internal `Channel<T>` for the handoff. Simplest operationally — one container, no transport.
- **(ii) One process, two identities.** A single binary holding two OAuth clients and using a different bearer per MCP call. Operationally one container, securely two identities. Pays for both costs without the deployment isolation that would justify them.
- **(iii) Two processes, two identities.** Two projects (`InfraGate.Planner` and `InfraGate.Executor`), two Keycloak clients (`infra-gate-planner` and `infra-gate-executor`), two scopes (`mcp:tools.propose` and `mcp:tools.execute`), a transport between them. Mirrors the Observer's "one role per project, one identity per process" pattern.

The Observer's `mcp:tools.readonly` scope established the relevant precedent: a compromised Observer binary cannot mutate the cluster because the gateway enforces scope. The same defense-in-depth argument applies twice for the Planner/Executor pair — a compromised Planner can propose plans but cannot execute (still gated by human approval), and a compromised Executor can execute *already-approved* plans but cannot propose new ones.

Option (i) loses that property entirely. Option (ii) preserves it at the cost of operational complexity without the deployment isolation that would justify the cost. Option (iii) preserves it and matches the Observer pattern.

## Decision

Autonomous remediation is implemented as **two separate processes**:

- `src/InfraGate.Planner/` — consumes `AnomalyHandoffBatch` via an HTTPS handoff, runs the LLM step, calls `propose_plan` on the gateway, hands a `RemediationProposal` off to the Executor.
- `src/InfraGate.Executor/` — consumes `RemediationProposalBatch` via an HTTPS handoff, calls `wait_for_plan_approval(planId)` then `execute_approved_plan(planId)`.

Each has its own Keycloak `client_credentials` identity, its own scope, its own audit identity, its own container, and its own Dockerfile. Both reuse `InfraGate.ClientCredentials` (ADR-0016) for the OAuth path.

| | Planner | Executor |
|---|---|---|
| Project | `InfraGate.Planner` | `InfraGate.Executor` |
| Keycloak client | `infra-gate-planner` | `infra-gate-executor` |
| Scope | `mcp:tools.propose` | `mcp:tools.execute` |
| Audit identity | `service:planner` | `service:executor` |
| Allowed gateway tools | `propose_plan` (+ read-only inspection) | `wait_for_plan_approval`, `execute_approved_plan` |
| Disallowed gateway tools | `execute_approved_plan`, `request_*`, read-write tools | `propose_plan`, `request_*`, read-write tools |

The transport between them is HTTPS push (`IAnomalyHandoffSink` on the Observer side pushes to the Planner; a sibling `IRemediationProposalSink` on the Planner side pushes to the Executor). Each consumer exposes one Minimal API endpoint; mutual auth uses the same `InfraGate.ClientCredentials` library.

## Consequences

- **Defense-in-depth is structural, not aspirational.** A compromised Planner cannot bypass the human approver and cannot execute. A compromised Executor cannot create new Plan Envelopes. Both properties are enforced by the gateway's per-tool scope check, not by application code in the agents themselves.
- **Operational cost is two containers** added to the `deploy/local-oauth/compose.yaml` stack, two Keycloak clients in the realm export, two sets of env vars in the run profiles, two Dockerfiles. The cost is real but bounded — each agent's deployment story is a near-copy of the Observer's.
- **The transport contract is a discipline.** Forcing an explicit handoff (HTTP body schema, sink interface, sink implementation) keeps Planner and Executor genuinely decoupled. Either side can be replaced — a smarter Planner using Opus, a thinner Executor with no LLM at all — without disturbing the other.
- **`InfraGate.ClientCredentials` (ADR-0016) earns a third consumer.** The library's existence is now justified beyond Observer + DownstreamAuth.
- **The decision *would* be wrong** if the autonomous loop were one short-lived feature with no extension path. The judgement that justifies it is the same as for the Observer: this is the second of several autonomous MCP clients the codebase will host, and the per-role-per-process discipline scales straightforwardly to a fourth or fifth.
- **One-process consolidation remains reversible** later if operational simplicity ever outweighs the scope-split benefit — merging two projects into one is a refactor, not a redesign. Splitting *back* from one process to two later would require re-creating identities, scopes, and the transport. The asymmetry argues for starting with the more separable shape.
