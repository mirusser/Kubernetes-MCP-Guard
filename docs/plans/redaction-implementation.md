# Plan: Log and Data Redaction — Section 11 Implementation

## Goal

Implement `SensitiveDataRedactor` that intercepts downstream tool responses
(`get_pod_logs`, `get_k8s_resource`, etc.) and redacts common secret patterns
before the response reaches the MCP client, with audit metadata (counts,
patterns matched, tool name) but **never storing the matched values**.

## Architecture Decision

**The spec says `src/InfraGate.McpServer/Redaction/` but the existing
sanitisation pipeline lives in `src/InfraGate.McpGateway/Guardrails/`.**

The redaction hook must be added to `SanitizingToolCaller.CallAsync()` and
`GuardedToolRunner.SanitizeAndAuditResponseAsync()` — both in McpGateway.
Placing `SensitiveDataRedactor` in McpGateway avoids a cross-project dependency
from McpGateway → McpServer and keeps all guardrail concerns in one layer.

- `SensitiveDataRedactor` and its supporting result/pattern types should be
  `internal` to `InfraGate.McpGateway`; tests access them through the existing
  `InternalsVisibleTo` entries.
- New convention constants must live in a nested class that does not collide
  with the existing `McpGatewayConventions.Redactions` class. Use
  `McpGatewayConventions.SensitiveDataRedaction` for the placeholder helper and
  pattern constants, and add the new category/action constants to the existing
  `GuardrailCategories` / `GuardrailAudit` classes.

**Location:** `src/InfraGate.McpGateway/Guardrails/Redaction/`

## Dependency Graph

```
     T1: RedactionConstants + RedactionPattern type
          └─→ T2: SensitiveDataRedactor
                 └─→ T3: Redaction audit wiring (GuardrailAuditEvent metadata)
                       └─→ T4: Inject into SanitizingToolCaller & GuardedToolRunner
                             └─→ T5: DI registration + runtime-mode guard
                                   └─→ T6: Tests
                                         └─→ T7: Doc alignment
```

T1 pattern constants and `RedactionPattern` are independent; T2 depends on T1;
T3 depends on T2; T4 depends on T1–T3; T5 depends on T4; T6 depends on T2 + T4;
T7 depends on T3/T4.

## Tasks

### T1 (S) — Redaction constants in McpGatewayConventions

**Files:**
- `src/InfraGate.McpGateway/McpGatewayConventions.cs`

**What:**
- Add `GuardrailAudit.RedactSensitiveDataAction = "redact_sensitive_data"`.
- Add `GuardrailCategories.SensitiveData = "sensitive-data"`.
- Add a nested `SensitiveDataRedaction` static class:
  - A `Placeholder(string patternName)` method that returns
    `$"[redacted: {patternName}]"`.
  - A `Patterns` nested static class containing the default regex strings
    (listed below).
  - A `Defaults` property or field that exposes the patterns as a read-only
    list of `RedactionPattern` records (name + regex), ordered from most
    specific to least specific.

| Pattern name | Regex |
|---|---|
| `private-key` | `-----BEGIN\s+(?:RSA|EC|OPENSSH|PRIVATE)\s+KEY-----` |
| `jwt` | `eyJ[a-zA-Z0-9_-]+\.eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+` |
| `aws-key` | `(?:AKIA|ASIA)[0-9A-Z]{16}` |
| `bearer-token` | `(?i)bearer\s+[a-zA-Z0-9\-._~+/]{20,}` |
| `basic-auth` | `(?i)basic\s+[a-zA-Z0-9=+/]{20,}` |
| `connection-string` | `(?i)(?:Server\|Host\|Data Source)\s*=.*[Pp]assword\s*=` |
| `password-param` | `(?i)password\s*=\s*\S+` |
| `secret-param` | `(?i)secret\s*=\s*\S+` |
| `token-param` | `(?i)token\s*=\s*\S+` |
| `api-key-param` | `(?i)api[_-]?key\s*=\s*\S+` |

- Add `GuardrailAudit.EntryFields.RedactionPatterns = "redactionPatterns"`.
- Add `GuardrailAudit.EntryFields.RedactionCount = "redactionCount"`.
- Reuse the existing `GuardrailAudit.EntryFields.ToolName` constant.

**Acceptance:**
- All new constants and the placeholder method are accessible from the
  `InfraGate.McpGateway` namespace.
- Pattern order is more-specific-first (`private-key`, `jwt`, `aws-key`,
  `bearer-token`, `basic-auth`, `connection-string`, then the generic `*-param`
  patterns).
- No existing `McpGatewayConventions.Redactions` constants are changed or
  removed.

**Verification:**
- `dotnet build src/InfraGate.McpGateway/` succeeds.
- A scratch assertion confirms the constants resolve:
  ```csharp
  Assert.Equal("redact_sensitive_data", McpGatewayConventions.GuardrailAudit.RedactSensitiveDataAction);
  Assert.Equal("sensitive-data", McpGatewayConventions.GuardrailCategories.SensitiveData);
  Assert.Equal("[redacted: aws-key]", McpGatewayConventions.SensitiveDataRedaction.Placeholder("aws-key"));
  ```

---

### T2 (S) — `RedactionPattern` type and `SensitiveDataRedactor`

**Files:**
- `src/InfraGate.McpGateway/Guardrails/Redaction/RedactionPattern.cs`
- `src/InfraGate.McpGateway/Guardrails/Redaction/RedactionResult.cs`
- `src/InfraGate.McpGateway/Guardrails/Redaction/SensitiveDataRedactor.cs`

**What:**
- `RedactionPattern` — `internal sealed record class RedactionPattern(string Name, string Regex)`.
- `RedactionResult` — `internal sealed record class RedactionResult`:
  - `string Text` — redacted output.
  - `bool WasRedacted` — true if any pattern matched.
  - `IReadOnlyDictionary<string, int> CountByPattern` — pattern name → count.
  - `IReadOnlyList<string> PatternsMatched` — distinct pattern names that
    matched, in discovery order.
- `SensitiveDataRedactor` — `internal sealed class`:
  - Constructor accepts `IReadOnlyList<RedactionPattern>`; when called with
    defaults it uses `McpGatewayConventions.SensitiveDataRedaction.Defaults`.
  - Compiles each pattern once with
    `RegexOptions.Compiled | RegexOptions.CultureInvariant` and the standard
    `McpGatewayConventions.RegexTimeoutMilliseconds` timeout.
  - `Redact(string text) → RedactionResult`:
    - Applies patterns sequentially in the order supplied.
    - Replaces each match with `McpGatewayConventions.SensitiveDataRedaction.Placeholder(name)`.
    - Tracks counts and distinct pattern names.
  - On `RegexMatchTimeoutException`, returns a result equivalent to the
    original input (`WasRedacted = false`) and logs a non-secret warning using
    the injected `ILogger<SensitiveDataRedactor>`; the matched text must never
    appear in the log.
- Thread-safe: compiled regexes are immutable.

**Do NOT:**
- Store the raw matched values in the result.
- Expose the matched text in any public/internal property.
- Log the matched values.

**Acceptance:**
- Input with AWS key → output contains `[redacted: aws-key]`.
- Input with `password=foo123` → output contains `[redacted: password-param]`.
- Input with JWT → output contains `[redacted: jwt]`.
- Input with private key PEM block → output contains `[redacted: private-key]`.
- Input with no secrets → output equals input, `WasRedacted = false`.
- Multiple matches of same pattern → `CountByPattern[pattern] > 1`.
- Multiple different patterns → `PatternsMatched` contains all distinct matches.
- A regex timeout is caught and the original text is returned without crashing.
- Matched secret values are not present in `RedactionResult` or logs.

**Verification:**
- `dotnet test tests/InfraGate.McpGateway.Tests/ --filter "SensitiveDataRedactor"` passes.

---

### T3 (S) — Redaction audit wiring

**Files:**
- `src/InfraGate.McpGateway/Guardrails/Audit/GuardrailAuditEvent.cs`
- `src/InfraGate.McpGateway/Guardrails/Audit/GuardrailAuditStore.cs`

**What:**
- Extend `GuardrailAuditEvent` with an optional metadata bag:

  ```csharp
  public sealed record class GuardrailAuditEvent(
      string ToolName,
      string Direction,
      string Action,
      string[] Categories,
      string? PlanId,
      string? Subject,
      string? AuthenticationType,
      string IdentityKind = "Human",
      IReadOnlyDictionary<string, object?>? Metadata = null);
  ```

  The `Metadata` bag is only set for redaction events and must contain only
  pattern names and counts — never the matched values.
- Update `GuardrailAuditStore.WriteAsync` to merge `auditEvent.Metadata`
  entries into the JSONL dictionary when the bag is not null:
  - `McpGatewayConventions.GuardrailAudit.EntryFields.RedactionPatterns` →
    `PatternsMatched`
  - `McpGatewayConventions.GuardrailAudit.EntryFields.RedactionCount` →
    `CountByPattern`
- Callers (`SanitizingToolCaller` and `GuardedToolRunner`) construct the
  metadata dictionary from `RedactionResult` when `WasRedacted` is true and
  pass it as the `Metadata` argument of `GuardrailAuditEvent`.

**Acceptance:**
- When redaction fires, the audit JSONL entry contains `redactionPatterns` and
  `redactionCount`.
- When redaction does NOT fire, those fields are absent from the audit entry.
- Matched values are NEVER present in the audit entry or the
  `GuardrailAuditEvent.Metadata` bag (verified by test).
- `IGuardrailAuditStore.WriteAsync` signature is unchanged, so existing test
  fakes and the file store continue to compile.

**Verification:**
- `GuardrailAuditStoreTests.WriteAsync_RedactionMetadata_*` pass.
- A scratch read of the JSONL audit file shows the new fields and no secret
  substrings.

---

### T4 (M) — Inject redaction into `SanitizingToolCaller` and `GuardedToolRunner`

**Files:**
- `src/InfraGate.McpGateway/Guardrails/SanitizingToolCaller.cs`
- `src/InfraGate.McpGateway/Guardrails/GuardedToolRunner.cs`
- `src/InfraGate.McpGateway/Guardrails/Sanitization/ResponseSanitizationResult.cs`

**What:**

Add constructor-injected dependencies to both callers:
- `SensitiveDataRedactor redactor`
- `McpGatewayOptions options` (used for the runtime-mode guard)

**Runtime-mode guard:** redaction runs in **all** runtime modes, including
`Production`. The `SensitiveDataRedactor` is invoked for every downstream
response after prompt-injection sanitization. Prompt-injection sanitization
continues unchanged.

**`SanitizingToolCaller.CallAsync()`:**
1. After `PromptInjectionGuard.SanitizeResponse`, run
   `redactor.Redact(sanitized.Text)`.
2. Use `redacted.Text` as the final returned text.
3. If `redacted.WasRedacted`, write a `GuardrailAuditEvent` with:
   - `Action = McpGatewayConventions.GuardrailAudit.RedactSensitiveDataAction`
   - `Categories = [McpGatewayConventions.GuardrailCategories.SensitiveData]`
   - `Metadata` from `RedactionResult`
4. If both prompt-injection sanitization and sensitive-data redaction fire,
   write the existing prompt-injection audit event **and** the new redaction
   audit event.

**`GuardedToolRunner.SanitizeAndAuditResponseAsync()`:**
1. After `PromptInjectionGuard.SanitizeResponse`, run
   `redactor.Redact(response.Text)`.
2. Update the returned `ResponseSanitizationResult.Text` to the redacted text.
3. Add a `bool SensitiveDataRedacted = false` optional property to
   `ResponseSanitizationResult`; set it to true when redaction matched.
4. If `redacted.WasRedacted`, write a `GuardrailAuditEvent` as described above.
5. If both prompt-injection sanitization and sensitive-data redaction fire,
   write both audit events.

**`GuardedToolRunner.CallForModelVisibleResponseAsync()`:**
- When `response.SensitiveDataRedacted` is true, add
  `McpGatewayConventions.GuardrailCategories.SensitiveData` to the merged
  `categories` list.
- Update `DetermineGuardrailAction` so that:
  - If prompt-injection findings exist, keep `warn_redact`.
  - Else if the response manifest was redacted, keep `redact_manifest`.
  - Else if `response.SensitiveDataRedacted` is true, return
    `McpGatewayConventions.GuardrailAudit.RedactSensitiveDataAction`.
  - Else if the request scan had findings, return `warn`.
  - Else return `allow`.

**Note:** `GuardedToolRunner.CallAsync` still prepends the prompt-injection
warning whenever `GuardedToolCallResult.Categories` is non-empty. Because the
production read-only path uses `CallForModelVisibleResponseAsync`, secret-only
redaction will not inject the prompt-injection warning into the final envelope.
If `CallAsync` is ever used for secret-bearing responses, revisit the warning
condition.

**Callers / tests:** the constructors of `SanitizingToolCaller` and
`GuardedToolRunner` gain new required parameters; update the production DI
factory and all manual `new(...)` sites in unit tests.

**Acceptance:**
- Response from `get_pod_logs` containing an AWS key → AWS key is replaced with
  `[redacted: aws-key]` in the final MCP response.
- Response from `get_k8s_resource` containing `password=xyz` → redacted.
- Production mode → redaction still runs; response is redacted and a
  sensitive-data audit event is written.
- When both prompt-injection and sensitive-data redaction fire, both audit
  events are written and both transformations are applied.
- The `GuardrailAction` in the model-visible envelope is
  `redact_sensitive_data` when only sensitive-data redaction fired.

**Verification:**
- `SanitizingToolCallerTests` and `GuardedToolRunnerTests` redaction scenarios
  pass.
- Production-mode tests verify redaction runs for both callers.

---

### T5 (S) — DI registration and runtime-mode guard

**Files:**
- `src/InfraGate.McpGateway/Configuration/ConfigurationExtensions.cs`

**What:**
- Register `SensitiveDataRedactor` as a singleton using the default pattern list:

  ```csharp
  builder.Services.AddSingleton<SensitiveDataRedactor>(_ =>
      new SensitiveDataRedactor(McpGatewayConventions.SensitiveDataRedaction.Defaults));
  ```

- Update the `AddSingleton<IToolCaller>(...)` factory to pass
  `sp.GetRequiredService<SensitiveDataRedactor>()` and
  `sp.GetRequiredService<McpGatewayOptions>()` into `SanitizingToolCaller`.
- `GuardedToolRunner` is registered with `AddSingleton<GuardedToolRunner>()`;
  its constructor will receive `SensitiveDataRedactor` and `McpGatewayOptions`
  automatically from DI.

**No new configuration key for v1:** redaction is always on. It is not gated
by `RuntimeMode`.

**Acceptance:**
- `SensitiveDataRedactor` and `McpGatewayOptions` are resolvable from the
  service provider.
- `SanitizingToolCaller` and `GuardedToolRunner` resolve without
  `InvalidOperationException`.
- No existing service registration is removed or reordered.

**Verification:**
- A DI smoke test resolves `SanitizingToolCaller`, `GuardedToolRunner`, and
  `SensitiveDataRedactor` from a configured `IServiceProvider`.

---

### T6 (M) — Tests

**Files:**
- `tests/InfraGate.McpGateway.Tests/UnitTests/SensitiveDataRedactorTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/SanitizingToolCallerTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GuardrailAuditStoreTests.cs`

**What:**

**`SensitiveDataRedactorTests`:**
- One theory/fact per default pattern verifying the placeholder appears and
  `WasRedacted` is true.
- `Redact_CleanInput_ReturnsOriginalTextAndWasRedactedFalse`
- `Redact_MultipleMatchesSamePattern_IncrementsCountByPattern`
- `Redact_MultipleDifferentPatterns_ListsAllPatternsMatched`
- `Redact_RegexTimeout_ReturnsOriginalText`
- `Redact_MatchedSecrets_DoNotAppearInResultOrLogs`

**`SanitizingToolCallerTests`:**
- Update `CreateCaller` to accept/pass `SensitiveDataRedactor` and
  `McpGatewayOptions`.
- `CallAsync_ResponseContainsSecret_RedactsAndWritesRedactionAuditEvent`
- `CallAsync_ProductionMode_RedactsAndAuditsSensitiveData`
- `CallAsync_PromptInjectionAndSecret_BothAuditsWritten`

**`GuardedToolRunnerTests`:**
- Update manual constructors to pass `SensitiveDataRedactor` and
  `McpGatewayOptions`.
- `SanitizeAndAuditResponseAsync_ResponseContainsSecret_RedactsAndAudits`
- `SanitizeAndAuditResponseAsync_ProductionMode_RedactsAndAudits`
- `CallForModelVisibleResponseAsync_OnlySensitiveDataRedaction_GuardrailActionIsRedactSensitiveData`
- `CallForModelVisibleResponseAsync_PromptInjectionAndSecret_CategoriesIncludeSensitiveData`

**`GuardrailAuditStoreTests`:**
- `WriteAsync_RedactionMetadata_WritesRedactionPatternsAndCount`
- `WriteAsync_RedactionMetadata_DoesNotContainMatchedValue`

**Acceptance:**
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "SensitiveDataRedactor"` passes.
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` all pass.

---

### T7 (XS) — Align security-model docs

**Files:**
- `docs/security-model.md`
- `src/InfraGate.McpGateway/README.md` (only if the guardrail audit entry
  description needs a one-line update)

**What:**
- In `docs/security-model.md` section 2.3, add the new
  `redact_sensitive_data` action and the `redactionPatterns` /
  `redactionCount` fields to the guardrail audit entry description.
- Keep the statement that matched secret values must never be stored.

**Acceptance:**
- `docs/security-model.md` accurately describes the new audit fields.
- No aspirational or unimplemented behavior is claimed.

**Verification:**
- `rg -n 'redact_sensitive_data|redactionPatterns|redactionCount' docs/security-model.md` matches.

## Non-Goals (Explicitly Out of Scope)

- Redacting request arguments (inbound) — only response redaction.
- Custom user-defined patterns in v1 (default patterns only).
- Configuration UI or runtime toggling beyond the existing runtime-mode guard.
- Redacting structured fields in JSON responses by path (only text regex
  replacement).
- Storing redacted values in audit — explicitly forbidden.
- Adding redaction to the McpServer side (kept in McpGateway per architecture
  decision above).

## Risks and Open Questions

| Risk | Impact | Mitigation |
|---|---|---|
| Redaction runs in `Production`, so false positives may mask legitimate data the model or user expects to see. | Medium | Document the default patterns in `docs/security-model.md`; add an opt-out configuration key only if requested after v1 ships. |
| Regex application on every downstream response adds latency in `Production`. | Medium | Keep patterns finite, compiled, and timeout-bounded; benchmark and consider pattern count limits if latency spikes. |
| On regex timeout the implementation returns the original text, which could leak secrets if a pathological response is hit in `Production`. | Medium | Log the timeout as a non-secret warning so operators can detect the condition; consider a fail-closed mode in a future iteration. |
| Adding `Metadata` to `GuardrailAuditEvent` changes the audit event shape. Tests and any downstream consumers of the JSONL file must tolerate new fields. | Low | New fields are additive; existing tests that ignore unknown JSON properties are unaffected. |
| `GuardedToolRunner.CallAsync` prepends the prompt-injection warning whenever categories are non-empty. Secret-only redaction through `CallAsync` would therefore prepend an inaccurate warning. | Low | The production read-only path uses `CallForModelVisibleResponseAsync`; revisit if `CallAsync` is put on a hot path. |

## Decisions

- Sensitive-data redaction runs in **all** runtime modes, including `Production`.
- When both prompt-injection and sensitive-data redaction fire, the
  model-visible `GuardrailAction` remains `warn_redact` and the `sensitive-data`
  category is added to the categories list. No composite action is introduced.

## Open Questions

_None._

## Verification Checklist

- [ ] `dotnet build src/InfraGate.McpGateway/` succeeds
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/` all pass
- [ ] T1 constants and placeholder resolve correctly
- [ ] T2 all pattern tests, timeout test, and secrets-never-stored test pass
- [ ] T3 audit JSONL contains `redactionPatterns`/`redactionCount`, never matched values
- [ ] T4 integration tests pass; production mode redacts and audits
- [ ] T5 DI resolution works; no pre-existing registrations broken
- [ ] T6 `dotnet test --filter SensitiveDataRedactor` passes
- [ ] T7 `docs/security-model.md` reflects the new audit fields
- [ ] LSP diagnostics clean on all changed files
