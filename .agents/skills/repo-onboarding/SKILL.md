---
name: repo-onboarding
description: Orient agents in the repository before broad investigations, repo navigation, or unfamiliar work. Use this skill to check codegraph health, read repo guidance, canonical CONTEXT glossary, inspect local skills, choose relevant README/docs, and avoid .agents/Plans unless historical planning context is explicitly requested.
---

# Repo Onboarding

Use this skill when you need to get oriented in `k8s-toolkit`, start a broad investigation, or find the right project context before working. Do not use it for narrow command-only questions or tiny edits that already have clear local context.

## Workflow

1. **Check codegraph health.** Run `codegraph_status` to confirm the index is available and roughly consistent (expect ~670 files, ~9000 nodes). If the index is missing or stale, fall back to file reads for all discovery steps below.

2. **Read `AGENTS.md`.** Follow its rules for surfacing assumptions, keeping changes simple, making surgical edits, and verifying the result.

3. **Read the canonical context language.**
   - If `CONTEXT-MAP.md` exists, use it to find the relevant context files.
   - Otherwise read root `CONTEXT.md` — the canonical glossary for the mutation-approval profile, generic approval core, domain adapters, and approval lifecycle terms.
   - Use `codegraph_search` to instantly locate any glossary term (interface, class, or type) in the index rather than grepping files manually.

4. **Get the project file tree with `codegraph_files`** (faster than `find`, returns only indexed source files, skips `actions-runner/` and other non-source trees automatically). Use `maxDepth: 3` for a high-level map; drill into a specific `path` for deeper exploration.

5. **Build task context with `codegraph_context`.** This is the primary entry point. Pass the task description and let it surface relevant entry points, symbols, and code snippets. Do this *before* deciding which READMEs to read — it often replaces step 6 entirely for code-focused tasks.

6. **Load relevant repo-local skills** from `.agents/skills/`:

   | Skill | When to load |
   |---|---|
   | `code-standards` | Code edits, reviews, refactors, convention work |
   | `writing-tests` | Adding or modifying tests, or tasks involving internal types |
   | `tdd` | Feature or bugfix work requiring test-first discipline |
   | `dotnet-a2a-agent` | Implementing A2A listeners/callers, task lifecycle, or agent handlers |
   | `dotnet-agent-workflows` | Building LLM agents (AIFunction tools) or DAG executor workflows |
   | `infragate-mcp-gateway` | Kubernetes or local MCP gateway inspection and guarded changes |
   | `verify-readme-docs` | README audits or documentation refreshes |
   | `review-mutation-approval-flow` | Mutation-approval glossary, flow diagrams, relationship table, profile sketch, ADR consistency |
   | `grill-with-docs` | Cross-referencing code behavior against docs |
   | `improve-codebase-architecture` | Structural or architectural refactor proposals |
   | `planning-and-task-breakdown` | Breaking down a large task before starting implementation |
   | `run-tests` | Running the test suite or debugging test failures |
   | `run-local-sonarqube` | Running SonarQube locally against the codebase |
   | `sonarcloud-remediation` | Addressing SonarCloud findings in CI |
   | `sonar-local-remediaton` | Addressing SonarQube findings from a local run |

7. **Targeted README reads** — only for context that `codegraph_context` did not cover (rationale, flow diagrams, architecture prose, ADR decisions):

   | What you need | Where to look |
   |---|---|
   | Project purpose, architecture, capabilities, project map | `README.md` |
   | Local setup, run commands, MCP tool contracts, verification | `docs/devs-readme.md` |
   | Mutation-approval profile, generic approval core, plan envelopes, domain adapters | `docs/mutation-approval-profile.md`, `docs/mutation-approval-flow.md` |
   | Roadmap direction | `docs/roadmap.md` and ADRs under `docs/adr/` |
   | MCP server, Kubernetes tools, validation, approval plans | `src/InfraGate.McpServer/README.md` |
   | HTTP gateway, forwarding, guardrails, sanitization, audit logging | `src/InfraGate.McpGateway/README.md` |
   | Gateway auth, bearer tokens, OAuth JWTs, protected-resource metadata, audit identity | `src/InfraGate.McpGateway.Auth/README.md` |
   | Approval core, challenge store, grant lifecycle, pre-execution gate | `src/InfraGate.Approvals/README.md` |
   | Kubernetes domain adapter, policy checks | `src/InfraGate.KubernetesAdapter/README.md`, `src/InfraGate.KubernetesAdapter/Policy/README.md` |
   | Run profiles CLI, env-file and appsettings rendering | `src/InfraGate.RunProfiles/README.md` |
   | Downstream auth: client credentials, token providers, McpServer integration | `src/InfraGate.DownstreamAuth/` (no README yet — read source directly; see `DownstreamAuthConventions`, `DownstreamAuthOptions`, `IDownstreamServiceTokenProvider`) |
   | Prompt Library, Handlebars templates, system prompts | `src/InfraGate.Prompts/README.md` |
   | Agent MCP Toolset, ReadOnly filtering, connection abstraction | `src/InfraGate.AgentMcp/README.md` |
   | Agent guardrails, tool-call middleware, hallucination metrics | `src/InfraGate.AgentGuardrails/README.md` |
   | Observability, tracing, metrics | `src/InfraGate.Observability/README.md` |
   | Runtime safety, env-var config, production guard | `src/InfraGate.RuntimeSafety/README.md` |
   | Test work | Matching `tests/*/README.md` |
   | Demo manifests | `examples/failing-deployment/README.md` |

8. **Before making any change: run `codegraph_impact`** on the target symbol to understand blast radius. Use `codegraph_callers` / `codegraph_callees` to trace call chains when the approval or dispatch flow is involved.

## Codegraph vs. Doc reads

Use codegraph for **what** and **where**: symbol locations, call chains, impact of a change.
Fall back to README and doc reads for **why**: design rationale, ADR decisions, flow diagrams, architecture prose.

## Discovery Guardrails

Exclude `.agents/Plans/**` during normal onboarding and discovery. That directory contains planning history, not current source-of-truth documentation. Read it only when the user explicitly asks for plans, roadmap details, or historical context.

When a file-system scan is unavoidable, prune both planning history and the self-hosted runner tree:

```bash
find . \( -path './.agents/Plans' -o -path './actions-runner' \) -prune \
  -o -iname '*readme*.md' -print | sort
```
