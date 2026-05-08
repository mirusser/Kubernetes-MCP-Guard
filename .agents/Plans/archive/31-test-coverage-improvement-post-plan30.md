# Implementation Plan: Test Coverage Improvement Post-Plan-30

## Status: Implemented

## Summary
Improved Sonar new-code coverage from 79% after Plan 30 implementation by adding tests for uncovered paths, removing dead code, and configuring Sonar exclusion for source-generated regex code.

## Issue
Sonar reported 79% test coverage on new code since commit `dd3571f` (Plan 30 implementation). Root cause split into two categories:

### Real gaps (now covered)
1. Pre-existing uncovered lines in `PromptInjectionGuard.Sanitization.cs` (JsonException catch, null JSON node, array element mutation)
2. Pre-existing uncovered branches in `K8sPolicyValidator.cs` (feature-flag-disabled early returns)
3. Dead code overload in `K8sManager.Requests.cs` (unused 2-param `CreateAndFormatPlanAsync`)

### False positives (documented as accepted)
1. Source-generated `[GeneratedRegex]` attribute in `PromptInjectionGuard.Regex.cs` — not instrumented by coverage tools
2. Async state machine compiler-generated continuation paths in `K8sManager.Requests.cs` — known .NET coverage artifact

## Tasks Completed

### Task 1: Sonar exclusion for source-generated regex ✓
Added `**/PromptInjectionGuard.Regex.cs` to `sonar.coverage.exclusions` in `.github/workflows/sonar.yml`.

### Task 2-4: ResponseSanitization gap tests ✓
Added 3 tests in `ResponseSanitizationTests.cs` covering:
- Malformed JSON → `JsonException` catch in `TryRedactJson`
- JSON array with null → `case null` in `RedactJsonNode`
- JSON array suspicious string → `jsonArray[i] = redacted` mutation in `RedactJsonArray`

### Task 5: GuardrailAuditStore tests (user addition) ✓
Added 3 tests in `GuardrailAuditStoreTests.cs` covering multi-event append, tool/direction/action/categories write, and planId serialization.

### Task 6: K8sPolicyValidator feature-flag-off tests ✓
Added 2 tests in `K8sPolicyValidatorTests.cs` covering:
- `DenyHostPathVolumes = false` early-return branch
- `DenyLatestImageTag = false` early-return branch

### Task 7: Remove dead code ✓
Removed unused 2-parameter `CreateAndFormatPlanAsync(K8sPlan, CancellationToken)` overload in `K8sManager.Requests.cs`.

### User-added coverage (commits `f20e22c`, `cbeb45d`)
- `DevIssuerStoreTests.cs` — 141 lines
- `K8sManagerStatusTests.cs` — 144 lines
- `K8sObjectNormalizerTests.cs` — 225 lines
- `K8sPolicyValidator.cs` refactoring (split monolith into `ValidateHostSettings`, `ValidateVolumes`, `ValidatePrivilegedFlag`, `ValidateCapabilities`, `ValidateImageTag`)

## Final Coverage State (local analysis)

| File | Coverage | Status |
|---|---|---|
| `PromptInjectionGuard.Sanitization.cs` | 238/238 (100%) | Done |
| `PromptInjectionGuard.Regex.cs` | 28/28 (100%) | Done + sonar excluded |
| `PromptInjectionGuard.cs` | 70/70 (100%) | Done |
| `K8sManager.Requests.cs` (CreateAndFormatPlanAsync) | 34/34 (100%) | Done |
| `K8sManager.Requests.cs` (CreateDryRunPlanAsync) | 60/60 (100%) | Done |
| `K8sManager.Requests.cs` (main class) | 100/100 (dead code removed) | Done |
| `K8sPolicyValidator.cs` | 268/276 → expected higher | Feature-flag-off tests added |
| `K8sManager.Requests.cs` (async state machines) | 81-91% | Accepted artifact |

## Accepted Exclusions
- Async state machine uncovered lines in `K8sManager.Requests.cs` — these are compiler-generated continuation paths in `async` methods, not meaningful source code. The source-level code IS covered; the state machine has unreachable compiler-generated transitions.
- Constant declarations in `K8sConventions.cs` and `McpGatewayConventions.cs` — these are `const string` declarations with no executable code. Sonar may miscount them as coverable lines.

## Verification
- `dotnet test tests/InfraGate.McpServer.Tests` — 159 passed, 0 failed
- `dotnet test tests/InfraGate.McpGateway.Tests` — 116 passed, 0 failed
