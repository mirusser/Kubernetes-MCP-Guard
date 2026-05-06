# Epic 9 Contributor And Maintenance Docs

## Summary

Add the missing external contributor and maintenance surface for Kubernetes MCP Guard. Keep it docs-only, visibly experimental, and link to existing owner docs instead of duplicating setup, security, OIDC, configuration, or architecture detail.

## Key Changes

- Add `CONTRIBUTING.md` with:
  - Project status and contribution expectations for an experimental security-sensitive Kubernetes MCP project.
  - Setup and verification commands: `dotnet build InfraGate.slnx`, `dotnet test InfraGate.slnx --no-build`, opt-in integration flags, `./scripts/coverage.sh`, Docker Compose validation/run pointers.
  - Safe MCP-tool contribution rules: no raw shell or `kubectl exec` passthrough, preserve OAuth gateway auth, namespace allow-listing, RBAC assumptions, plan-first mutations, hash-bound approvals, bounded observability, and guardrail/audit behavior.
  - Pointers to `docs/devs-readme.md`, `docs/setup-guide.md`, `docs/security-model.md`, `docs/tool-permissions.md`, `docs/configuration.md`, and `SECURITY.md`.
- Add `.github/PULL_REQUEST_TEMPLATE.md` with required sections for summary, verification, docs, and a safety checklist covering MCP tools, mutation behavior, auth/scope, RBAC/namespace assumptions, approval/hash flow, guardrails/audit, tests, and demo impact.
- Add Markdown issue templates:
  - `.github/ISSUE_TEMPLATE/bug_report.md`: repro steps, expected/actual behavior, run mode, version/image tag/commit, client, Kubernetes/minikube details, logs with secrets redacted, and security-report reminder.
  - `.github/ISSUE_TEMPLATE/feature_request.md`: problem, proposal, alternatives, safety impact checklist, docs/tests/demo expectations.
- Add `CHANGELOG.md` in Keep a Changelog style with an `Unreleased` section and an initial entry for Epic 9 docs; do not backfill detailed notes for existing `v0.0.1`-`v0.0.4` tags.
- Add `docs/roadmap.md` as the public-facing slim roadmap:
  - Experimental status, current capabilities, recent documentation/security foundations, near-term contributor/readiness work, and future hardening such as release-smoke CI, SBOM/provenance/signing, and broader production guidance.
  - No internal `.agents/Plans` links or detailed implementation history.
- Update `README.md` links:
  - Add `CONTRIBUTING.md`, `CHANGELOG.md`, and `docs/roadmap.md` to the project/governance area.
- Update `docs/releasing.md` so release checklist and release-note template reference `CHANGELOG.md` entries.

## Interfaces

- No runtime API, MCP tool contract, schema, environment variable, CI workflow, or C# code changes.
- New public repository-maintenance surfaces are the contributor guide, PR template, issue templates, changelog, and public roadmap.

## Test Plan

- Run `git diff --check`.
- Verify expected files exist:
  - `test -f CONTRIBUTING.md CHANGELOG.md docs/roadmap.md .github/PULL_REQUEST_TEMPLATE.md .github/ISSUE_TEMPLATE/bug_report.md .github/ISSUE_TEMPLATE/feature_request.md`
- Verify README/release cross-links:
  - `rg -n 'CONTRIBUTING.md|CHANGELOG.md|docs/roadmap.md' README.md docs/releasing.md`
- Verify safety-sensitive PR prompts:
  - `rg -n 'MCP tools|mutation behavior|auth|RBAC|approval|guardrails|audit|tests|demo' .github/PULL_REQUEST_TEMPLATE.md`
- Verify public docs do not expose internal planning paths:
  - `rg -n '\\.agents/Plans' README.md CONTRIBUTING.md CHANGELOG.md docs/roadmap.md .github`
- No `dotnet test` required unless implementation unexpectedly changes code or workflows.

## Assumptions

- Skip optional `CODE_OF_CONDUCT.md` for this Epic 9 pass; it can be a later governance decision.
- Use Markdown issue templates, not GitHub YAML issue forms.
- Start `CHANGELOG.md` from `Unreleased` rather than reconstructing historical release notes.
- Keep `docs/roadmap.md` high-level and non-committal; no dates or production-certification promises.
