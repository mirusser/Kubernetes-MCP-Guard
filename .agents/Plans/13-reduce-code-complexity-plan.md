# Plan: Make Coverage Complexity Metrics Green With Production-Only Refactors

## Summary
Refactor only production code, not tests, to reduce ReportGenerator risk hotspots for `Cyclomatic complexity` and `Crap Score`. Do not hide code with extra coverage exclusions. Keep behavior and public contracts unchanged.

Current report has 20 risk-hotspot methods. Treat “green” as ReportGenerator’s generated metric state: no `"exceeded": true` entries in the `riskHotspots` block after `./scripts/coverage.sh`.

## Key Changes
- In `K8sManager`, split high-complexity methods into small private helpers:
  - Approval/apply flow: break `RequestServerApprovalAsync`, `ApplyObjectAsync`, `WaitForDeploymentsAsync`, and `FormatApiException` into single-purpose helpers.
  - Kubernetes summary builders: convert large anonymous-object expression methods into block-bodied methods with local `metadata/spec/status` variables and small helpers so each reported method stays below the complexity threshold.
  - Resource routing: replace the guarded switch in `GetResourceAsync` with a low-complexity private dispatch helper.
  - Image update planning: extract deployment-container lookup/current-image comparison helpers from `RequestSetDeploymentImageAsync`.

- In prompt-injection guard code:
  - Extract dictionary/array traversal helpers from `ScanValue`, `ScanJsonNode`, and `RedactJsonNode`.
  - Keep traversal order, location strings, and redaction behavior unchanged.

- In gateway auth/dev issuer:
  - Move OAuth setup details out of `AddGatewayAuthentication` into private helpers.
  - Split `GatewayAuditIdentityResolver.Resolve` into unauthenticated/static/OAuth subject helpers.
  - Split `DevIssuerApplication.Authorize` validation into small private validation helpers while preserving every existing error string and redirect/token behavior.

- Keep any untested extracted helper at cyclomatic complexity `<= 5`, so its CRAP score remains green even without direct test coverage. Keep covered helpers at `<= 15`.

## Verification
- Run `dotnet test InfraGate.slnx`.
- Run `./scripts/coverage.sh`.
- Confirm `coverage-report/index.html` opens and risk-hotspot metrics are green.
- Machine-check the report with:
  ```bash
  perl -0ne 'exit 1 if /var riskHotspots = \[(.*?)\];/s && $1 =~ /"exceeded": true/' coverage-report/main.js
  ```
- Run `git diff --check`.

## Assumptions
- “Don’t modify tests itself” means no edits under `tests/`; running tests and coverage is allowed.
- No new public APIs, MCP tool names, auth contracts, response shapes, or coverage exclusions should be introduced.
- Minimal change means small private method extraction and local-variable simplification only, not broader architecture cleanup.
