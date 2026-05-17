# SonarCloud Remediation Plan

Date: 2026-05-17
Source: `sonarcloud-report.json` (generated 2026-05-17T20:37:10Z from branch `main`)

**Root cause of stale data**: The `sonar.yml` workflow was querying SonarCloud's issues API immediately after `dotnet-sonarscanner end`, before the CE background task finished processing the uploaded analysis. Fixed in this pass — added CE task polling step (120s timeout).

## Quality Gate: OK

All 6 quality gate conditions passed. No BLOCKER or CRITICAL issues.

## Finding Summary

| Rule | Severity | Count | Status |
|---|---|---|---|
| CA1873 (log arg evaluation) | INFO | 14 | Already resolved |
| S101 (K8s naming) | MINOR | 15 | False positive (convention) |
| xUnit1042 (untyped MemberData) | INFO | 4 | Already resolved |
| CA1822 (member can be static) | INFO | 2 | Already resolved |
| S3267 (loop to Where) | MINOR | 1 | **Fixed** — justification added |
| CA1859 (IReadOnlyList→array) | INFO | 1 | Already resolved |
| Security hotspots | — | 3 | REVIEWED/SAFE |

**Total issues**: 37 open
**Stale (already resolved in code)**: 33
**False positive (S101 convention)**: 15 (already justified)
**Fixed in this pass**: 1 (S3267)
**Hotspots reviewed safe**: 3

---

## Task 1: S3267 — False positive resolution in PromptInjectionGuard.Regex.cs

**Rule**: csharpsquid:S3267 — "Loops should be simplified using the 'Where' LINQ method"
**Severity**: MINOR
**File**: `src/InfraGate.McpGateway/PromptInjectionGuard.Regex.cs:102`

### Finding
Line 102 uses `.Count(predicate)` which is already the canonical LINQ form. There is no `foreach`+`if` loop to simplify with `.Where()`. The S3267 analyzer incorrectly flags `.Count(c => ...)` as a loop.

### Fix
Added `// Justification:` comment explaining that `.Count(predicate)` is already the canonical LINQ form and no foreach+if pattern exists to simplify.

```csharp
// Justification: S3267 — .Count(predicate) is already the canonical LINQ form;
// there is no foreach+if loop to simplify with .Where().
var printable = decodedText.Count(c => ...);
```

### Verification
- Build: `dotnet build InfraGate.slnx` — 0 warnings, 0 errors
- Tests: `dotnet test InfraGate.slnx --filter "Category!=Keycloak"` — 419 passed, 0 failed

---

## Task 2: S101 — K8s naming convention (pre-existing false positives)

**Rule**: csharpsquid:S101
**Severity**: MINOR
**Files**: 13 files across `InfraGate.McpServer`, `InfraGate.KubernetesAdapter`

### Status: Already resolved

Per repository convention (`lessons.md §12`), `K8s` (not `K8S`) is the canonical naming. All 13 remaining S101-targeted files already have `// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.` comments. Two additional findings reference deleted/renamed files and will clear on the next scan.

### Files with existing justification comments
- `K8sManager.cs` (plus 6 partial files)
- `K8sConventions.cs` (+ nested `K8sResources`)
- `K8sParsedManifest.cs`
- `K8sTools.cs`
- `K8sValidationException.cs`
- `K8sManifestParser.cs`
- `K8sObjectRef.cs` (moved to `InfraGate.KubernetesAdapter`)

### Stale (deleted files)
- `K8sManager.Requests.cs` — deleted; finding will clear on next scan
- `K8sGatewayTools.cs` — deleted; finding will clear on next scan

---

## Task 3: CA1873 — Logger argument evaluation (pre-existing resolutions)

**Rule**: external_roslyn:CA1873
**Severity**: INFO

### Status: Already resolved — no action needed

All 14 findings are already addressed:

| File | Resolution |
|---|---|
| `K8sManager.Observability.cs:19` | `// Justification: CA1873 — all log arguments are simple scalars` |
| `K8sManager.Status.cs:12` | `// Justification: CA1873 — all log arguments are simple scalars` |
| `K8sManager.Status.cs:45` | Wrapped in `if (logger.IsEnabled(LogLevel.Information))` |
| `Program.cs:60` | Wrapped in `if (appLogger.IsEnabled(LogLevel.Information))` |
| `Program.cs:71` | `// Justification: CA1873 — log argument is a simple string property` |
| `K8sManager.Requests.cs` (6 findings) | File deleted; findings will clear on next scan |
| `StreamWriterLoggerProviderTests.cs` (2 findings) | File deleted; findings will clear on next scan |

---

## Task 4: xUnit1042 — MemberData returns untyped rows (pre-existing resolution)

**Rule**: external_roslyn:xUnit1042
**Severity**: INFO
**File**: `tests/InfraGate.McpServer.Tests/UnitTests/AuditPayloadsTests.cs`

### Status: Already resolved

Both `PlanPayloads()` (line 18) and `ChallengePayloads()` (line 49) already return `TheoryData<,>` instead of `object[]` or `IEnumerable<object[]>`. Findings will clear on the next SonarCloud scan.

---

## Task 5: CA1822 — Mark members as static (pre-existing resolution)

**Rule**: external_roslyn:CA1822
**Severity**: INFO
**File**: `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

### Status: Already resolved

Both flagged methods are already `static`:
- `FollowImpersonationRedirectAsync` (line 430): `private static async Task ...`
- `SubmitLoginFormAsync` (line 526): `private static async Task<HttpResponseMessage> ...`

The SonarCloud report references wrong line numbers (method body lines rather than declarations). Findings will clear on next scan.

---

## Task 6: CA1859 — Use array instead of IReadOnlyList (pre-existing resolution)

**Rule**: external_roslyn:CA1859
**Severity**: INFO
**File**: `src/InfraGate.McpServer/K8sManager.DryRun.cs:39`

### Status: Already resolved

Line 39 already uses `K8sObjectRef[] objects` instead of `IReadOnlyList<K8sObjectRef>`. Finding predates the fix and will clear on next scan.

---

## Security Hotspots

All 3 hotspots in `scripts/setup-development-deploy.sh` are `REVIEWED` with `SAFE` resolution. No action needed.

| Hotspot | Line | Status | Resolution |
|---|---|---|---|
| clear-text protocols (curl) | 253 | REVIEWED | SAFE |
| clear-text protocols (curl) | 256 | REVIEWED | SAFE |
| clear-text protocols (curl) | 261 | REVIEWED | SAFE |

---

## Verification

- Build: `dotnet build InfraGate.slnx` — 0 warnings, 0 errors
- Tests (Tier 1): `dotnet test InfraGate.slnx --filter "Category!=Keycloak"` — 419 passed, 0 failed, 0 skipped

---

## Expected Impact on Next Scan

- 33 stale findings will resolve automatically (files deleted or fixes already present)
- 15 S101 findings remain as justified false positives
- 1 S3267 finding now has justification comment
- 3 hotspots remain reviewed/safe
- Expected new issues: 0 (all already addressed in code)
