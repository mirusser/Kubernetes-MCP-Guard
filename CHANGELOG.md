# Changelog

All notable changes to Kubernetes MCP Guard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses pre-release tags while it remains experimental.

## [Unreleased]

### Added

- Contributor and maintenance documentation for Epic 9: contributor guide, pull request checklist, issue templates, public roadmap, and changelog.
- Server-side Kubernetes `dryRun=All` validation before approval plan creation and again at apply time. Mutation plans record dry-run results in the hash-bound pending plan; the gateway approval page renders dry-run status; legacy plans without dry-run data are refused.
- Diff-before-approval: every mutation plan records a server-generated diff between normalized live Kubernetes state and proposed dry-run state. The browser approval page renders the diff, policy findings, and JSON-path changes before approval. Apply-time drift enforcement re-reads live objects and refuses mutation if live state has changed since approval.
- Opt-in file logging for the MCP server via `K8S_MCP_LOG_PATH` environment variable. When set, all log output is written to the specified file (structured JSON via Serilog) in addition to the stderr transport. No file is created when the variable is unset.
- `ToolExceptionFilter` safety net: an MCP request filter wraps every tool invocation, catching unhandled exceptions (especially `TaskCanceledException` from HTTP timeouts) and returning a proper `IsError=true` tool response instead of crashing the server.
- Gateway now forwards its full environment to the downstream MCP server subprocess via `StdioClientTransportOptions.EnvironmentVariables`, ensuring `K8S_MCP_*` env vars reach the child process.
- `GuardedToolRunner` now catches downstream MCP client exceptions and returns formatted error text instead of letting them bubble to HTTP 500s.
- New `deploy/mode-d/` Docker Compose files: local-build (`compose.yaml`) and published-image (`compose.release.yaml`) variants with Keycloak for OAuth testing.
- Keycloak realm expanded with `default-roles-infra-gate`, client scopes (`profile`, `email`, `roles`, `basic`, `acr`), and additional redirect URIs for localhost development.

### Fixed

- `TaskCanceledException` from Kubernetes API HTTP timeouts no longer escapes per-method catch blocks as an unhandled exception; the safety net now catches it and returns a proper error response.
- `KubernetesConfigProvider` now wraps kubeconfig load failures in `InvalidOperationException` with the file path and underlying error message.
- Improved Keycloak health check from `curl` to TCP probe, reducing cold-start startup time.

## [v0.0.9] — 2026-05-23

### Added

- PostgreSQL persistence for generic approval storage.
- Downstream MCP stdio authentication: service token provider, server-side token validation, secret redaction, one-retry on rejection, E2E coverage.
- MCP Approval Notification via Resource Subscriptions.
- `get_plan_status` tool for checking plan state.
- Typed configuration binding and env-file-driven Docker configuration.
- Centralized `build.props`, analyzer rules, and critical warning fixes.
- Local SonarQube analysis setup.
- Safety E2E tests proving the seven approval-flow safety properties.
- Approval plan UI: card-based dark theme with rendered diffs, policy findings, and JSON-path changes.
- Keycloak Compose stack and OAuth integration tests.
- Production mode with fail-closed deployment checks.
- Reduced model-visible sensitive output.
- Safer server-side apply defaults (no `force`).
- SonarCloud report artifact export and remediation skills.

### Changed

- Approval flow generalization: separate generic approval core from Kubernetes adapter, digest-bound approval grants, opaque plan identifiers.
- Gateway–domain adapter separation.
- Configuration migrated to run profiles with appsettings binding.
- DevIssuer deprecated in favor of Keycloak parity.
- Serilog structured logging across gateway and server.
- `DownstreamMcpClient` switched from full env passthrough to explicit allowlist.
- Reduced brittle assertions in tests.

### Fixed

- Naming consistency and proper diff display in plan review.
- DI wiring for tests and downstream auth filter (`GetService` vs `GetRequiredService`).
- Integration test string mismatches, missing Docker dependencies.
- Protection key persistence, container permissions, Docker image builds.

## [v0.0.6] — 2026-05-14

### Added

- Observer, Planner, and Executor agent skeleton: remediation proposal handoff, HTTP wrapper, observability, deployment path, integration tests.
- Approval plan UI: card-based dark theme with rendered fields and rendering tests.
- Keycloak Compose stack and integration tests (Auth-Code + PKCE).
- Production mode with fail-closed deployment checks.
- Safer server-side apply defaults.
- Reduced model-visible sensitive output.
- SonarCloud report export and remediation skill.
- Development CI/CD deployment on self-hosted Keycloak runner.
- End-to-end approval demo video and demo manifests.

## [v0.0.5] — 2026-05-08

### Added

- Diff-before-approval: server-generated Kubernetes diff rendered in the browser approval page.
- Server-side `dryRun=All` validation before plan creation and at apply time.
- Policy validator for mutation plans.
- Security model documentation, production OIDC guide, configuration reference.
- Contributor and maintenance documentation.
- Full architectural diagram, plans, and skill cleanup.

### Changed

- Approval challenge storage moved into the Approvals project.

## [v0.0.4] — 2026-05-04

### Added

- Image and dependency scanning (Trivy, CodeQL).
- Extended test coverage.

### Changed

- Switched to chiseled ASP.NET runtime images for smaller base images.

### Fixed

- Vulnerability fixes.

## [v0.0.3] — 2026-05-03

### Added

- GitHub Actions packaging workflow.
- Repository polish: README, LICENSE, SECURITY.md.
- Roadmap draft and initial plans.

## [v0.0.1] / [v0.0.2] — 2026-05-03

### Added

- MCP HTTP gateway with guardrails and sanitization.
- OAuth authentication with repo-local DevIssuer and JWT bearer.
- Kubernetes MCP server tools for cluster observability.
- MCP transport specification compliance.
- End-to-end approval demo with demo manifests and setup guide.
- Test infrastructure: integration tests, coverage reporting, minikube checks.
- GitHub Actions CI workflows.
- Code conventions, standards, and documentation.

## Release History

Earlier experimental tags exist (`v0.0.1` through `v0.0.4`), but detailed historical changelog entries have not been reconstructed. Use GitHub Releases and commit history for those versions.
