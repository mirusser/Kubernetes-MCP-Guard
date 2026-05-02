# Kubernetes MCP Guard 🛡️: AI-safe Kubernetes operations through MCP

Kubernetes MCP Guard is a .NET 10 gateway/server for AI-assisted Kubernetes operations through the Model Context Protocol.

It lets MCP clients such as Codex, Open WebUI, and LibreChat inspect clusters, propose changes, and apply only approved mutations through OAuth-aware authentication, prompt-injection guardrails, audit logging, namespace-scoped RBAC, bounded observability, and exact-plan approval checks.

![Tests](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/unit-tests.yml/badge.svg?branch=main)
![Tests](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/integration-tests.yml/badge.svg?branch=main)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=mirusser_Kubernetes-MCP-Guard&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=mirusser_Kubernetes-MCP-Guard)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mirusser_Kubernetes-MCP-Guard&metric=coverage)](https://sonarcloud.io/summary/new_code?id=mirusser_Kubernetes-MCP-Guard)

[![SonarQube Cloud](https://sonarcloud.io/images/project_badges/sonarcloud-dark.svg)](https://sonarcloud.io/summary/new_code?id=mirusser_Kubernetes-MCP-Guard)

## What It Does ⚙️

- Exposes Kubernetes operations through the Model Context Protocol (MCP), with a stdio Kubernetes server behind a local HTTP gateway.
- Uses the Kubernetes API via `KubernetesClient`; it does not shell out to `kubectl` for runtime operations.
- Adds OAuth/static bearer authentication at the gateway, including MCP protected-resource metadata and insufficient-scope challenges.
- Applies prompt-injection guardrails around model-visible tool input/output, with warn, redact, and audit behavior.
- Keeps mutation paths approval-gated: create a plan first, then apply the exact approved plan.
- Limits Kubernetes blast radius with namespace-scoped RBAC and typed, bounded tool surfaces.

```mermaid
flowchart TB
    Client["MCP client<br/>Codex / Open WebUI / LibreChat"]

    subgraph Gateway["HTTP MCP Gateway"]
        Auth["OAuth or bearer auth"]
        Guardrails["Prompt-injection guardrails"]
        Audit["Guardrail audit log"]
        Auth --> Guardrails
        Guardrails --> Audit
    end

    subgraph Server["Kubernetes MCP Server"]
        Tools["Typed Kubernetes tools"]
        ReadOnly["Bounded read-only observability"]
        Plans["Approval-gated mutation plans"]
        Tools --> ReadOnly
        Tools --> Plans
    end

    subgraph Kubernetes["Kubernetes boundary"]
        RBAC["Namespace-scoped RBAC"]
        API["Kubernetes API"]
        RBAC --> API
    end

    Client --> Auth
    Guardrails --> Tools
    ReadOnly --> RBAC
    Plans --> RBAC
```
<sub><em>Simplified architectural graph. Full version [here](docs/MCP-compliance.md)</em></sub>

## Why It Matters 🔐

AI infrastructure tools are powerful, but power is not the same thing as safety. Kubernetes MCP Guard treats AI-assisted ops as a systems design problem: capability needs identity, boundaries, auditability, and human approval at the point where state changes.

That makes the project a practical slice of a bigger direction: MCP-native infrastructure operations where agents can inspect, explain, and propose, while production-grade controls decide what actually changes.

## What This Demonstrates 🚀

- AI systems knowledge: MCP transports, tool contracts, elicitation-style approval, and prompt-injection risk around model-visible data.
- Security judgment: OAuth resource-server behavior, scope enforcement, protected-resource metadata, audit identity, and least-privilege RBAC.
- Kubernetes fluency: typed API usage, server-side apply planning, rollout checks, Events, Pod logs, resource summaries, and namespace isolation.
- Product engineering taste: small operational surface, clear user flows, safety defaults, and readable documentation for humans and agents.
- Modern .NET implementation: .NET 10, dependency injection, async APIs, focused tests, and project-level separation of auth, gateway, server, issuer, and test concerns.

## How To Run ▶️

The recommended local OAuth setup runs the gateway and dev issuer with Docker Compose; the gateway launches the Kubernetes MCP server privately over stdio:

```bash
./scripts/create-demo-kubeconfig.sh --compose
docker compose -f deploy/mode-c/compose.yaml up --build
```

Codex can then connect to `http://127.0.0.1:3001/mcp` with OAuth:

```toml
[mcp_servers.infra-gate]
url = "http://127.0.0.1:3001/mcp"
oauth_resource = "http://127.0.0.1:3001/mcp"
scopes = ["mcp:tools"]
```

```bash
codex mcp login infra-gate
```

*For source-based run modes and verification details, see the [Setup Guide](docs/setup-guide.md).*

## Current Capabilities 🧰

### Gateway Protections 🛡️

| Layer | Behavior |
|---|---|
| Authentication | OAuth 2.1 JWT or local static bearer token |
| Prompt-injection guardrails | Warn and redact suspicious model-visible input/output |
| Audit logging | JSONL guardrail audit with identity resolution |
| MCP compliance | Streamable HTTP transport, protected-resource metadata, step-up authorization |

### Read-Only Observability 🔎

| Tool | Purpose |
|---|---|
| `get_k8s_status` | Deployments, Services, ConfigMaps, Pods, and ReplicaSets in a namespace |
| `get_k8s_events` | Bounded `events.k8s.io/v1` cluster diagnostics |
| `get_pod_logs` | Bounded Pod log reads (tail lines + byte cap) |
| `get_k8s_resource` | Focused resource summary — no Secret values, ConfigMap data, or raw manifests |
| `get_deployment_diagnostics` | Deployment health, related Pods, ReplicaSets, and Events |
| `get_pod_diagnostics` | Pod status, conditions, container state, and Events |
| `get_service_diagnostics` | Service endpoints, backing Pods, and Events |

### Approval-Gated Mutations ✅

| Tool | Purpose |
|---|---|
| `request_apply_manifest` | Plan a server-side apply for `Deployment`, `Service`, or `ConfigMap` |
| `request_delete_manifest` | Plan a resource deletion |
| `request_scale_deployment` | Plan a replica count change |
| `request_restart_deployment` | Plan a rollout restart |
| `request_set_deployment_image` | Plan a container image update |
| `apply_approved_plan` | Apply an exact-hash-verified, user-approved plan |

## Explore The Project 🧭

- Developer runbook: [docs/devs-readme.md](docs/devs-readme.md)
- Setup guide: [docs/setup-guide.md](docs/setup-guide.md)
- MCP compliance notes: [docs/MCP-COMPLIANCE.md](docs/MCP-COMPLIANCE.md)
- Kubernetes MCP server: [src/InfraGate.McpServer/README.md](src/InfraGate.McpServer/README.md)
- HTTP MCP gateway: [src/InfraGate.McpGateway/README.md](src/InfraGate.McpGateway/README.md)
- Gateway auth: [src/InfraGate.McpGateway.Auth/README.md](src/InfraGate.McpGateway.Auth/README.md)
- Local dev OAuth issuer: [src/InfraGate.DevIssuer/README.md](src/InfraGate.DevIssuer/README.md)
