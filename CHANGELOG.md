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

## Release History

Earlier experimental tags exist (`v0.0.1` through `v0.0.4`), but detailed historical changelog entries have not been reconstructed. Use GitHub Releases and commit history for those versions.
