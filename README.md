# 🛡️ Kubernetes MCP Guard 

**Bridging the gap between AI Agents and Production Infrastructure with a Security-First Gateway.**


[![Unit Tests](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/unit-tests.yml/badge.svg?branch=main)](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/unit-tests.yml)
[![Integration Tests](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/integration-tests.yml/badge.svg?branch=main)](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/integration-tests.yml)
[![Docker](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/package-docker.yml/badge.svg?branch=main)](https://github.com/mirusser/Kubernetes-MCP-Guard/actions/workflows/package-docker.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=mirusser_Kubernetes-MCP-Guard&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=mirusser_Kubernetes-MCP-Guard)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mirusser_Kubernetes-MCP-Guard&metric=coverage)](https://sonarcloud.io/summary/new_code?id=mirusser_Kubernetes-MCP-Guard)

<sub>![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet) ![Kubernetes](https://img.shields.io/badge/Kubernetes-Cloud--Native-326ce5?style=flat-square&logo=kubernetes) ![Docker](https://img.shields.io/badge/Docker-Containerized-2496ed?style=flat-square&logo=docker) ![AI/LLM](https://img.shields.io/badge/AI/LLM-MCP%20Ready-black?style=flat-square&logo=openai)</sub>


## 🎯 The Problem
Giving AI agents direct access to Kubernetes is risky. Without a safety layer, an LLM "hallucination" or a prompt injection could accidentally delete a production namespace or leak sensitive secrets.

## 🚀 The Solution
Kubernetes-MCP-Guard is a high-performance gateway built on .NET 10 and the Model Context Protocol (MCP). It provides a secure, audited, and human-gated interface that allows AI agents (like Claude Code or Open WebUI) to interact with your clusters without compromising safety.

## 💎 Key Business Value

- **Human-in-the-Loop Governance:** AI can propose changes, but only a human can approve the final execution plan — via a separate browser-based approval flow that the MCP client cannot intercept or answer on the human's behalf.
- **Enterprise-Grade Security:** Integrated with OAuth-aware authentication and Namespace-scoped RBAC to ensure the AI only sees what it is allowed to see.
- **AI Safety & Auditing:** Built-in prompt-injection guardrails and full audit logging for compliance and troubleshooting
- **Cutting-Edge Stack:** Architected using the latest .NET 10 features and the industry-standard MCP protocol.

---

## 🗺️ System Architecture

The following diagram illustrates the secure request flow from the AI client through the Guardrails and into the Kubernetes cluster.

```mermaid
---
title: Kubernetes-MCP-Guard Flow
---
flowchart TB
    Client["MCP client<br/>Codex / Open WebUI / LibreChat"]

    subgraph Gateway["HTTP MCP Gateway"]
        Auth["OAuth JWT auth"]
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


### 🔐 How Approval-Gated Mutations Work

The diagram below shows what happens when an AI agent tries to make a change to your cluster. The key point: **the AI cannot approve its own requests**. Approval happens in your browser, on a completely separate channel with its own login.

```mermaid
---
title: Out-of-Band Approval Flow
---
flowchart TB
    classDef ai      fill:#e8f0fe,stroke:#4285f4,color:#1a1a2e,font-size:13px
    classDef browser fill:#e6f4ea,stroke:#34a853,color:#1a1a2e,font-size:13px
    classDef gate    fill:#fff3e0,stroke:#fb8c00,color:#1a1a2e,font-size:13px
    classDef k8s     fill:#fce4ec,stroke:#e53935,color:#1a1a2e,font-size:13px

    subgraph AI["① AI / MCP Channel"]
        direction TB
        A1["🤖 AI agent requests a change e.g. scale deployment to 3 replicas"]
        A2["Gateway validates identity; server dry-runs and creates a pending plan locked with a SHA-256 hash"]
        A3["⛔ AI receives an approval URL only.
         It cannot approve on your behalf"]
        A4["AI calls apply again once human has approved"]
    end

    subgraph OOB["② Your Browser  —  separate login, separate session"]
        direction TB
        B1["🔗 You open the approval URL in your browser"]
        B2["You log in with OAuth independent of the AI session"]
        B3["Browser shows the real plan rendered by the Gateway from disk not by the AI"]
        B4["You review: operation, namespace, affected objects, expiry time"]
        B5["✅ You click Approve  or  ❌ Deny"]
    end

    K8s["☸️ Kubernetes Change is applied only after both channels agree"]

    A1 --> A2 --> A3
    A3 -.->|"URL shown to AI, opened by you"| B1
    B1 --> B2 --> B3 --> B4 --> B5
    B5 -->|"Approval recorded with identity binding"| A4
    A4 --> K8s

    class A1,A2,A3,A4 ai
    class B1,B2,B3,B4,B5 browser
    class K8s k8s
```

<sub><em>Even if the AI agent is compromised, it cannot self-approve. The approval must come from your browser session a channel the AI has no control over.</em></sub>
<sub><em>Simplified architectural graph. Full version [here](docs/architecture.md)</em></sub>

### 🛠️ Technical & Architectural Highlights

- **AI Integration & Safety:** Deep implementation of Model Context Protocol (MCP), handling tool contracts, out-of-band approval workflows, and mitigating prompt-injection risks within model-visible data.

- **Security & Identity:** Implemented OAuth 2.0 resource-server patterns, including scope enforcement, identity-based audit logging, and strict Least-Privilege RBAC for Kubernetes interactions.

- **Cloud-Native Engineering:** Advanced usage of the Official Kubernetes .NET Client, utilizing Server-Side Apply (SSA) for dry-run planning, real-time Pod log streaming, and namespace isolation.

- **Modern .NET 10 Stack:** Built with a focus on Clean Architecture, leveraging Dependency Injection, high-performance Async/Await patterns, and a modular project structure that separates Auth, Gateway, and Core logic.

- **Product Mindset:** Prioritized a "small operational surface" and human-readable audit logs, ensuring the tool is as safe for a human SRE as it is powerful for an AI agent.

##### 🏗️ Implementation Details:
- Exposes Kubernetes operations through the Model Context Protocol (MCP), with a stdio Kubernetes server behind a local HTTP gateway.
- Uses the Kubernetes API via `KubernetesClient`; it does not shell out to `kubectl` for runtime operations.
- Adds OAuth authentication at the gateway, including MCP protected-resource metadata, insufficient-scope challenges, and browser approval sessions.
- Applies prompt-injection guardrails around model-visible tool input/output, with warn, redact, and audit behavior.
- Keeps mutation paths approval-gated: create a plan first, approve it in the Gateway browser UI, then apply the exact approved plan.
- Limits Kubernetes blast radius with namespace-scoped RBAC and typed, bounded tool surfaces.

## 📦 Container Images

Images are automatically built and scanned by the Docker workflow. Release tags publish versioned images, and the `dev` branch publishes moving `:dev` images for the self-hosted development deployment.

| Registry | Gateway (Core) | Dev Issuer (Auth) |
| --- | --- | --- |
| GitHub (GHCR) | `ghcr.io/mirusser/kubernetes-mcp-guard-gateway:<tag>` | `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:<tag>` |
| Docker Hub | `mirusser/kubernetes-mcp-guard-gateway:<tag>` | `mirusser/kubernetes-mcp-guard-devissuer:<tag>` |

**Versioning:** Use specific tags (e.g., `:v0.1.0`) for production stability. The `:dev` tag tracks the development branch, and the `:latest` tag tracks the most recent stable release.

Example pull:
``` bash
docker pull ghcr.io/mirusser/kubernetes-mcp-guard-gateway:latest
```

## ⚡ Quick Start

### Option 1 — Run from published images (no build required)

**Prerequisites:** [Docker Compose v2](https://docs.docker.com/compose/install/), [minikube](https://minikube.sigs.k8s.io/docs/start/), `git`.

```bash
git clone https://github.com/mirusser/Kubernetes-MCP-Guard.git

cd Kubernetes-MCP-Guard

./scripts/create-demo-kubeconfig.sh --compose
TAG=latest docker compose -f deploy/mode-c/compose.release.yaml up
```

Replace `latest` with a specific release tag (e.g. `v0.1.0`) for a stable run. Available tags are listed on the [Releases page](https://github.com/mirusser/Kubernetes-MCP-Guard/releases).

#### **Connect Codex CLI:**

1. Add this block to `~/.codex/config.toml` (create the file if it does not exist):

```toml
[mcp_servers.infra-gate]
url = "http://127.0.0.1:3001/mcp"
oauth_resource = "http://127.0.0.1:3001/mcp"
scopes = ["mcp:tools"]
```

2. Then log in:

```bash
codex mcp login infra-gate # authenticate 
codex # run codex
```

#### **Connect Claude Code:**

```bash
# 1. Add/register the MCP server
claude mcp add-json --scope user infra-gate \
  '{"type":"http","url":"http://127.0.0.1:3001/mcp","oauth":{"scopes":"mcp:tools"}}'

# 2. Start Claude Code
claude

# 3. Inside Claude Code, open MCP manager/auth flow
/mcp
```

 After successful log in you may start with: 
 ```text
 Explain briefly what are the capabilities of MCP server: infra-gate
 ```

### Option 2 — Build and run from source

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Docker Compose v2, minikube, `git`.

```bash
./scripts/create-demo-kubeconfig.sh --compose
docker compose -f deploy/mode-c/compose.yaml up --build
```

Connect Codex the same way as Option 1.

*Other run modes and full setup details are in the [Setup Guide](docs/setup-guide.md).*

## 🧰 Current Capabilities 

### 🛡️ Gateway Protections 

| Layer | Behavior |
|---|---|
| Authentication | OAuth 2.1 JWT for MCP plus browser OAuth cookie for approvals |
| Prompt-injection guardrails | Warn and redact suspicious model-visible input/output |
| Audit logging | JSONL guardrail audit with identity resolution |
| MCP compliance | Streamable HTTP transport, protected-resource metadata, step-up authorization |

### 🔎 Read-Only Observability 

| Tool | Purpose |
|---|---|
| `get_allowed_namespaces` | Namespace allow-list the server is configured to access |
| `get_k8s_status` | Deployments, Services, ConfigMaps, Pods, and ReplicaSets in a namespace |
| `get_k8s_events` | Bounded `events.k8s.io/v1` cluster diagnostics |
| `get_pod_logs` | Bounded Pod log reads (tail lines + byte cap) |
| `get_k8s_resource` | Focused resource summary — no Secret values, ConfigMap data, or raw manifests |
| `get_deployment_diagnostics` | Deployment health, related Pods, ReplicaSets, and Events |
| `get_pod_diagnostics` | Pod status, conditions, container state, and Events |
| `get_service_diagnostics` | Service endpoints, backing Pods, and Events |

### ✅ Approval-Gated Mutations 

| Tool | Purpose |
|---|---|
| `request_apply_manifest` | Dry-run and plan a server-side apply for `Deployment`, `Service`, or `ConfigMap` |
| `request_delete_manifest` | Dry-run and plan a resource deletion |
| `request_scale_deployment` | Dry-run and plan a replica count change |
| `request_restart_deployment` | Dry-run and plan a rollout restart |
| `request_set_deployment_image` | Dry-run and plan a container image update |
| `apply_approved_plan` | Repeat dry-run, then apply an exact-hash-verified, user-approved plan |

## 🎬 See It In Action 

End-to-end walkthrough of the approval-gated workflow against a deliberately broken Deployment: [docs/demo-failing-deployment.md](docs/demo-failing-deployment.md). Uses the demo manifests under [examples/failing-deployment/](examples/failing-deployment/).

## Compatibility

| Area | Supported / tested |
| --- | --- |
| .NET | .NET 10 |
| Kubernetes | minikube / local cluster initially |
| MCP transport | HTTP MCP endpoint at `/mcp` |
| OIDC | DevIssuer (local demo), Keycloak (development deploy), Entra ID later |
| Container registries | GHCR, Docker Hub |
| Platforms | linux/amd64 initially |

## 🧭 Explore The Project 

- Developer runbook: [docs/devs-readme.md](docs/devs-readme.md)
- Setup guide: [docs/setup-guide.md](docs/setup-guide.md)
- Configuration reference: [docs/configuration.md](docs/configuration.md)
- MCP compliance notes: [docs/MCP-compliance.md](docs/MCP-compliance.md)
- Security model: [docs/security-model.md](docs/security-model.md)
- Tool permissions matrix: [docs/tool-permissions.md](docs/tool-permissions.md)
- Production OIDC guide: [docs/production-oidc.md](docs/production-oidc.md)
- Public roadmap: [docs/roadmap.md](docs/roadmap.md)
- Kubernetes MCP server: [src/InfraGate.McpServer/README.md](src/InfraGate.McpServer/README.md)
- HTTP MCP gateway: [src/InfraGate.McpGateway/README.md](src/InfraGate.McpGateway/README.md)
- Gateway auth: [src/InfraGate.McpGateway.Auth/README.md](src/InfraGate.McpGateway.Auth/README.md)
- Local dev OAuth issuer: [src/InfraGate.DevIssuer/README.md](src/InfraGate.DevIssuer/README.md)

<sub><em>**Naming note:** The public name is **Kubernetes MCP Guard**. The internal codename **InfraGate** appears in `.slnx`, project folders, env-var prefixes (`INFRA_GATE_*`), and Docker labels. They refer to the same project; the rename is gradual and does not change runtime behavior.</em></sub>


## ⚖️ Governance & Policies

- License: [Apache-2.0](LICENSE)
- Security policy: [SECURITY.md](SECURITY.md)
- Contributing guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Release process: [docs/releasing.md](docs/releasing.md)

---
