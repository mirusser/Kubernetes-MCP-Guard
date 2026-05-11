# SonarCloud Remediation Plan

## Context

The `sonarcloud-report.json` (branch `dev`, generated 2026-05-10) contains 46 open issues across 27 files. Quality gate is **OK**. The goal is to resolve all actionable findings while correctly identifying the pervasive `csharpsquid:S101` K8s→K8S findings as false positives in this Kubernetes domain codebase.

---

## Issue Inventory

| Priority | Rule | Severity | Count |
|---|---|---|---|
| 1 | S2365 | CRITICAL | 2 |
| 1 | S2701 | CRITICAL | 1 |
| 2 | S107 | MAJOR | 1 |
| 2 | S1172 | MAJOR | 3 |
| 2 | kubernetes:S6897 | MAJOR | 2 |
| 3 | S101 (K8s→K8S) | MINOR | 20 (FALSE POSITIVE) |
| 3 | S1192 | MINOR | 1 |
| 3 | S3267 | MINOR | 3 |
| 3 | S2325 | MINOR | 3 |
| 3 | S1075 | MINOR | 3 (FALSE POSITIVE) |
| 4 | CA1859 | INFO | 10 |
| 4 | CA1861 | INFO | 1 |
| 4 | CA1869 | INFO | 3 |
| 4 | CA1822 | INFO | 1 |

---

## Tasks (ordered by priority)

### Task 1 — Fix collection-copying `Categories` property (S2365) — CRITICAL ✅

**Rule**: Properties must not copy collections on every access (allocates a new `string[]` per call).
**Files**:
- [src/InfraGate.McpGateway/GuardScanResult.cs](src/InfraGate.McpGateway/GuardScanResult.cs)
- [src/InfraGate.McpGateway/ResponseSanitizationResult.cs](src/InfraGate.McpGateway/ResponseSanitizationResult.cs)

**Fix applied**: Lazy backing field (`_categories ??= ...`) keeps the property API unchanged while making allocation explicit.

---

### Task 2 — Fix trivially-true assertion (S2701) — CRITICAL ✅

**Rule**: `Assert.Equal(true, expr)` is always a trivially-true assertion.
**File**: [tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs](tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs) — line 183

**Fix applied**:
```csharp
// Before
Assert.Equal(true, downstream.Arguments["previous"]);

// After
Assert.True((bool)downstream.Arguments["previous"]!);
```

---

### Task 3 — Remove unused method parameters (S1172) — MAJOR ✅

**Rule**: Unused parameters are dead code.
**File**: [src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs](src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs)

**Fix applied**: Removed `options` from `DefaultChallengeScheme` and both params from `ForwardedAuthenticationScheme`; updated call sites (lambda wrapper for the delegate-assigned method).

---

### Task 4 — Add storage requests to example YAML manifests (kubernetes:S6897) — MAJOR ✅

**Rule**: Containers without storage resource requests cannot be scheduled predictably.
**Files**:
- [examples/failing-deployment/deployment.yaml](examples/failing-deployment/deployment.yaml)
- [examples/failing-deployment/fix.yaml](examples/failing-deployment/fix.yaml)

**Fix applied**: Added `ephemeral-storage: 100Mi` to `resources.requests` in both files.

---

### Task 5 — Reduce K8sPlan constructor parameter count (S107) — MAJOR ✅

**Rule**: Constructors with > 7 parameters are hard to use correctly.
**File**: [src/InfraGate.Approvals/K8sPlan.cs](src/InfraGate.Approvals/K8sPlan.cs)

**Fix applied**: Replaced 11-param positional record with parameterless constructor + `init` properties. STJ .NET 7+ deserializes via init properties without `[JsonConstructor]`. A 7-param constructor covers the required fields; optional fields (`Manifest`, `DryRun`, `Diffs`, `PolicyFindings`) are set via object initializer at call sites.

All call sites updated:
- `src/InfraGate.McpServer/K8sManager.Requests.cs`
- `tests/InfraGate.McpServer.Tests/UnitTests/K8sDiffServiceTests.cs`
- `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerApplyTests.cs` (3 sites)
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`
- `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs`

---

### Task 6 — Define constant for repeated dry-run error string (S1192) — MINOR ✅

**Rule**: String literals repeated 5+ times should be defined as a constant.
**File**: [src/InfraGate.McpServer/K8sManager.DryRun.cs](src/InfraGate.McpServer/K8sManager.DryRun.cs)

**Fix applied**: Added `private const string DryRunFailedMessage = "Server-side dry-run failed";` and replaced all 5 occurrences.

---

### Task 7 — Simplify loops with LINQ Where (S3267) — MINOR ✅

**Rule**: A `foreach` loop that immediately filters with an `if` inside should use `.Where()`.
**File**: [src/InfraGate.McpServer/Policy/K8sPolicyValidator.cs](src/InfraGate.McpServer/Policy/K8sPolicyValidator.cs)

**Fix applied**: Three loops refactored to use `.Where()`.

---

### Task 8 — Make instance methods static (S2325 + CA1822) — MINOR/INFO ✅

**Rule**: Methods that don't access instance state should be declared `static`.
**Files**:
- [src/InfraGate.McpGateway/PromptInjectionGuard.Sanitization.cs](src/InfraGate.McpGateway/PromptInjectionGuard.Sanitization.cs) — `SanitizeResponse`
- [src/InfraGate.McpGateway/PromptInjectionGuard.Scanning.cs](src/InfraGate.McpGateway/PromptInjectionGuard.Scanning.cs) — `ScanArguments`
- [src/InfraGate.McpServer/K8sManager.Requests.cs](src/InfraGate.McpServer/K8sManager.Requests.cs) — `CreatePlan`

**Fix applied**: Added `static` to all three. Updated all callers:
- Production callers in `GuardedToolRunner.cs` use `PromptInjectionGuard.Method()` type-qualified calls
- Test callers in `PromptInjectionGuardTests.cs` and `ResponseSanitizationTests.cs` updated to type-qualified calls

---

### Task 9 — Use concrete types in parameter and return type declarations (CA1859) — INFO ✅

**Fix applied** (private/internal only):

| File | Change |
|---|---|
| [src/InfraGate.McpGateway/GatewayApprovalEndpoints.cs](src/InfraGate.McpGateway/GatewayApprovalEndpoints.cs) | `IReadOnlyList<K8sPlanPolicyFinding>` → `K8sPlanPolicyFinding[]`, `IReadOnlyList<K8sPlanDiff>` → `K8sPlanDiff[]`, `IReadOnlyList<string>` → `string[]` |
| [src/InfraGate.McpServer/Diff/K8sDiffService.cs](src/InfraGate.McpServer/Diff/K8sDiffService.cs) | `IReadOnlyDictionary<string, K8sPlanDryRunObject>` → `Dictionary<string, K8sPlanDryRunObject>` |
| [src/InfraGate.McpServer/K8sManager.Apply.cs](src/InfraGate.McpServer/K8sManager.Apply.cs) | Return types → `Task<V1Deployment>`, `Task<V1Service>`, `Task<V1ConfigMap>` |
| [src/InfraGate.McpServer/K8sManager.Diagnostics.cs](src/InfraGate.McpServer/K8sManager.Diagnostics.cs) | `IReadOnlySet<RelatedObjectRef>` → `HashSet<RelatedObjectRef>` |
| [src/InfraGate.DevIssuer/DevIssuerApplication.Metadata.cs](src/InfraGate.DevIssuer/DevIssuerApplication.Metadata.cs) | `IDictionary<string,object?>` → `Dictionary<string,object?>` |
| [tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs](tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs) | `SecurityKey` → `SymmetricSecurityKey`, `IReadOnlyDictionary<string,string[]>` → `Dictionary<string,string[]>` |

**Note**: When changing `IReadOnlyList<T>` → `T[]`, also update `.Count` → `.Length` at call sites — arrays use `Length`, not `Count`.

---

### Task 10 — Promote inline array to static readonly field (CA1861) — INFO ✅

**File**: [src/InfraGate.McpServer/Diff/K8sDiffService.cs](src/InfraGate.McpServer/Diff/K8sDiffService.cs)

**Fix applied**: `private static readonly string[] DiffHeaderLines = ["--- live", "+++ proposed"];` already added at class level; inline literal replaced.

---

### Task 11 — Cache `JsonSerializerOptions` in test files (CA1869) — INFO ✅

**Files**:
- [tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs](tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs) — added `private static readonly JsonSerializerOptions JsonWebIndented`
- [tests/InfraGate.McpServer.Tests/IntegrationTests/McpServerIntegrationTests.cs](tests/InfraGate.McpServer.Tests/IntegrationTests/McpServerIntegrationTests.cs) — added `private static readonly JsonSerializerOptions JsonWeb`

---

### Task 12 — Document S101 K8s naming as intentional (FALSE POSITIVE) — MINOR ✅

**Why this is a false positive**: `K8s` is the universally accepted industry abbreviation for Kubernetes. `K8S` is non-standard.

**Fix applied**: Added `// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.` before every `K8s*` type declaration across:
- All 6 files in `src/InfraGate.Approvals/`
- `src/InfraGate.McpServer/K8sManager.cs` (main partial file only; partials share the declaration)
- `src/InfraGate.McpServer/K8sConventions.cs` (outer class + nested `K8sApi` and `K8sResources`)
- `src/InfraGate.McpServer/Diff/K8sDiffService.cs` and `K8sObjectNormalizer.cs`
- All 4 files in `src/InfraGate.McpServer/Policy/` (`K8sPolicyFinding`, `K8sPolicyOptions`, `K8sPolicyResult`, `K8sPolicySeverity`, `K8sPolicyValidator`)
- `src/InfraGate.McpServer/K8sManifestParser.cs`, `K8sParsedManifest.cs`, `K8sTools.cs`, `K8sValidationException.cs`
- `src/InfraGate.McpGateway/K8sGatewayTools.cs`

**Flag for human review**: Consider adding a SonarCloud exclusion rule for S101 on `K8s*`-prefixed names in `sonar-project.properties` to prevent this noise in future scans.

---

### Task 13 — Document S1075 hardcoded dev URIs as intentional (FALSE POSITIVE) — MINOR ✅

**Why these are false positives**: Both URIs are in `*Conventions.cs` files dedicated to development/local defaults. These are `public const` values serving as documented defaults, not magic strings in business logic.

**Fix applied**: Added `// Justification: Intentional localhost default(s) for local development. S1075 false positive on documented convention constant(s).` on:
- [src/InfraGate.DevIssuer/DevIssuerConventions.cs](src/InfraGate.DevIssuer/DevIssuerConventions.cs) — above the three localhost constants
- [src/InfraGate.McpGateway/McpGatewayConventions.cs](src/InfraGate.McpGateway/McpGatewayConventions.cs) — above `DefaultUrl`

---

## Verification

```
dotnet build   → Build succeeded. 1 Warning, 0 Errors
dotnet test    → Passed: 347, Failed: 0, Skipped: 0
```

All 347 tests pass across 5 test assemblies. Next SonarCloud scan should show 0 actionable issues (S101/S1075 false-positive findings will remain visible but with justification comments, and are flagged for a rule exclusion review).
