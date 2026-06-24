# Plan: Remediate F-10 — Subprocess Binary Integrity Not Verified

**Date:** 2026-06-22
**Goal:** Make the MCP Gateway verify the SHA-256 hash of the downstream server binary before spawning it, and refuse startup when the binary does not match the pinned expected hash.

## Context

Finding **F-10** in `.agents/Plans/loose/security-audit.md` is the only remaining **unmitigated High** finding from the 2026-06-05 re-assessment. The gateway spawns `InfraGate.McpServer` as a stdio subprocess via `BootstrapStdioClientTransport` / `DownstreamMcpClient`, using either:

* `InfraGate__Gateway__DownstreamAssembly` — a published DLL path passed to `dotnet <assembly>` (preferred for production).
* `InfraGate__Gateway__DownstreamProject` — a `.csproj` path passed to `dotnet run --project` (development fallback).

Today there is no startup check that the binary on disk matches an expected value. A supply-chain or local-privilege-escalation attack that replaces the DLL would be invisible to the gateway.

The recommended fix from the audit is:

1. At startup, compute the SHA-256 hash of the subprocess binary.
2. Compare it to a pinned expected value stored outside the subprocess directory (config/env).
3. Refuse to start if the hash does not match.
4. (Hardening) Run the subprocess under a dedicated OS user with no write permission to its binary path; sign and verify the binary where possible.

This plan covers items 1–3. Item 4 is deployment/OS-level and is captured as a documentation follow-up.

## Request

Implement binary-integrity verification for the downstream MCP server so that F-10 can be marked **mitigated** in the next audit re-assessment.

### Acceptance criteria

* A new `InfraGate__Gateway__DownstreamAssemblyHash` setting accepts the expected SHA-256 hex digest.
* When `DownstreamAssembly` is configured and a hash is supplied, the gateway computes the file hash at startup and compares it using fixed-time comparison.
* In `Production` runtime mode, `DownstreamAssembly` **must** be configured and **requires** a hash; startup fails with a clear error if either is missing or if the hash mismatches.
* In `Development` mode, hash verification is opt-in: missing hash is allowed, but a supplied hash is still verified.
* The verification is covered by unit tests for match, mismatch, missing file, malformed hash, and case-insensitive hex.
* Run-profile generation supports the new setting.
* Configuration docs and the production run profile are updated.

## Plan

### Phase 1: Configuration and conventions

* [x] **Task 1 — Add configuration constants.**
  * Add `DownstreamAssemblyHash` to `McpGatewayConventions.ConfigurationKeys` (`InfraGate:Gateway:DownstreamAssemblyHash`).
  * Add `DownstreamAssemblyHash` to `McpGatewayConventions.EnvironmentVariables` (`InfraGate__Gateway__DownstreamAssemblyHash`).
  * **Verification:** The new constants compile and are referenced by Task 2.

* [x] **Task 2 — Bind the setting.**
  * Add `string? DownstreamAssemblyHash { get; init; }` to `InfraGateGatewaySettings`.
  * Add `string? DownstreamAssemblyHash = null` to the `McpGatewayOptions` positional record (place it after `DownstreamAssembly`) and populate it in `FromConfiguration` from the gateway settings.
  * Update any production call sites that construct `McpGatewayOptions` directly (e.g., opt-in E2E tests running in `Production` mode) to supply the new required values; existing Development-mode call sites are unaffected because the parameter is optional.
  * **Verification:** `InfraGateGatewaySettingsTests` and the binding assertions in `McpGatewayOptionsTests` pass.

* [x] **Task 3 — Add run-profile support.**
  * Add `DownstreamAssemblyHash` constant to `RunProfileConventions.YamlKeys` and `RunProfileConventions.Env`.
  * Add `string? DownstreamAssemblyHash` to `GatewayProfile`.
  * Read the value in `RunProfileDocumentReader.ReadGateway`, add the key to `KnownGatewayKeys`, and pass the value to the `GatewayProfile` constructor.
  * Render the env var in `EnvFileRenderer.AppendGateway`.
  * Merge the value in `RunProfileDocument.MergeGateway`.
  * Add `downstreamAssemblyHash` to `RunProfileCli.ApplyGatewayOverride` so `--set gateway.downstreamAssemblyHash=...` works.
  * **Verification:** Run-profile reader, renderer, and CLI `--set` tests added in Task 4 pass.

* [x] **Task 4 — Update existing settings tests.**
  * Add binding coverage in `InfraGateGatewaySettingsTests`.
  * Add run-profile reader/renderer coverage in `RunProfileDocumentReaderTests`, `EnvFileRendererTests`, and `RunProfileCliTests`.
  * Update `RunProfileCliTests.ComposeStackProfileKeys` to include `InfraGate__Gateway__DownstreamAssemblyHash` now emitted by the `production` profile.
  * Add merge coverage for `DownstreamAssemblyHash` in `RunProfileDocumentTests` (defaults do not supply it; profile value wins when present).
  * **Verification:** `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj` passes.

### Phase 2: Binary integrity verification

* [x] **Task 5 — Create the verifier module.**
  * Add a new internal seam under `src/InfraGate.McpGateway/BinaryIntegrity/` (one top-level type per file per repo convention):
    * `IDownstreamBinaryIntegrityVerifier` with one synchronous method: `void Verify(string assemblyPath, string expectedHashHex);`.
    * `DownstreamBinaryIntegrityException` carrying `FilePath` and `Algorithm` (`SHA-256`).
    * `sealed` `Sha256DownstreamBinaryIntegrityVerifier` adapter that:
      * Rejects null/empty/whitespace `expectedHashHex` and any value whose length is not exactly 64 hex characters with `FormatException`.
      * Parses the expected hex with `Convert.FromHexString` (case-insensitive); let `FormatException` propagate for malformed hex.
      * Opens the file with `File.OpenRead` and streams it through `SHA256.Create()` (do not use `File.ReadAllBytes`).
      * Compares the two byte arrays with `CryptographicOperations.FixedTimeEquals`.
      * Throws `DownstreamBinaryIntegrityException` on mismatch or missing file, setting `FilePath` to the supplied path.
    * `FakeDownstreamBinaryIntegrityVerifier` in `tests/InfraGate.McpGateway.Tests/Fakes/` that accepts a dictionary of `(path, expectedHash) -> bool`, exposes an `IReadOnlyList<(string Path, string ExpectedHash)> Calls` property, and throws `DownstreamBinaryIntegrityException` on mismatch.
  * Keep this module single-purpose: `McpGatewayOptions` decides **when** to verify; this module decides **how**.
  * **Verification:** `Sha256DownstreamBinaryIntegrityVerifierTests` from Task 9 pass and the fake can drive `McpGatewayOptionsTests` without file I/O.

* [x] **Task 6 — Integrate verification at startup.**
  * Change `McpGatewayOptions.ValidateProductionSafety()` to accept an optional `IDownstreamBinaryIntegrityVerifier? binaryIntegrityVerifier = null` parameter; when null, use `new Sha256DownstreamBinaryIntegrityVerifier()`.
  * Pass `DownstreamAssembly` to the verifier as configured; `File.OpenRead` resolves relative paths from the current working directory, matching `dotnet <assembly>` behavior.
  * `ConfigurationExtensions.AddInfraGateServices()` calls `options.ValidateProductionSafety()` with no arguments; the default verifier keeps the synchronous startup contract intact.
  * **Verification:** Gateway project builds and `ConfigurationExtensions.AddInfraGateServices()` resolves the default verifier without DI changes.

* [x] **Task 7 — Enforce production requirement.**
  * In `ValidateProductionSafety`, when `RuntimeMode == Production`:
    * If `DownstreamAssembly` is null, empty, or whitespace, throw `InvalidOperationException` referencing `InfraGate__Gateway__DownstreamAssembly`.
    * If `DownstreamAssemblyHash` is null, empty, or whitespace, throw `InvalidOperationException` referencing `InfraGate__Gateway__DownstreamAssemblyHash`.
    * If both are set, call the verifier and let its exception propagate on mismatch.
  * In Development mode, call the verifier only when both `DownstreamAssembly` and `DownstreamAssemblyHash` are non-empty; do not require either.
  * **Verification:** The production-safety tests listed in Task 8 pass.

* [x] **Task 8 — Update production-safety tests.**
  * Update `McpGatewayOptionsTests.BuildProductionConfig()` to set:
    * `McpGatewayConventions.ConfigurationKeys.DownstreamAssembly` to a stable dummy path (e.g., `/app/server/InfraGate.McpServer.dll`).
    * `McpGatewayConventions.ConfigurationKeys.DownstreamAssemblyHash` to a dummy 64-character hex string.
  * In each production-safety test, inject the fake verifier by passing it to `ValidateProductionSafety(fakeVerifier)`.
  * Add/update tests in `McpGatewayOptionsTests`:
    * `ValidateProductionSafety_ProductionWithAssemblyAndMatchingHash_AllowsStartup`
    * `ValidateProductionSafety_ProductionWithAssemblyButNoHash_ThrowsInvalidOperationException` — also assert the fake verifier received no calls.
    * `ValidateProductionSafety_ProductionWithAssemblyAndMismatchedHash_ThrowsDownstreamBinaryIntegrityException`
    * `ValidateProductionSafety_ProductionWithoutAssembly_ThrowsInvalidOperationException`
    * `ValidateProductionSafety_DevelopmentWithAssemblyAndNoHash_AllowsStartup`
    * `ValidateProductionSafety_DevelopmentWithAssemblyAndHash_MismatchedHash_ThrowsDownstreamBinaryIntegrityException`
  * Keep real-file hashing in `Sha256DownstreamBinaryIntegrityVerifierTests`; `McpGatewayOptionsTests` must not touch the filesystem.
  * **Verification:** `McpGatewayOptionsTests` passes, including the existing production happy-path tests.

### Phase 3: Verification unit tests

* [x] **Task 9 — Add `Sha256DownstreamBinaryIntegrityVerifierTests`.**
  * `Verify_KnownBytes_ReturnsWithoutThrowing` — temp file with known bytes, matching lower-case SHA-256.
  * `Verify_KnownBytesUpperCaseHash_ReturnsWithoutThrowing` — matching upper-case SHA-256.
  * `Verify_MismatchedHash_ThrowsDownstreamBinaryIntegrityException` — assert exception `FilePath` and `Algorithm` properties, not message text.
  * `Verify_MissingFile_ThrowsDownstreamBinaryIntegrityException`.
  * `Verify_MalformedHex_ThrowsFormatException` before hashing.
  * `Verify_EmptyHex_ThrowsFormatException`.
  * `Verify_WrongLengthHex_ThrowsFormatException` — expected hash hex length is not 64 characters (e.g., 62-character valid hex).
  * **Verification:** `Sha256DownstreamBinaryIntegrityVerifierTests` passes.

### Phase 4: Documentation and deployment

* [x] **Task 10 — Update `docs/configuration.md`.**
  * Add a row for `InfraGate__Gateway__DownstreamAssemblyHash` in the McpGateway table:
    * Required: Required in Production (because `DownstreamAssembly` is required in Production).
    * Default: Unset.
    * Example: `<64-character lower-case hex>`.
    * Description: Expected SHA-256 hex digest of the downstream server assembly.
    * Production guidance: Compute the hash from the file inside the runtime image, not a host bind-mount. Update on every server upgrade.
  * Add a one-line operator example: `sha256sum /app/server/InfraGate.McpServer.dll` (Linux) or `Get-FileHash -Algorithm SHA256 ...` (PowerShell).
  * **Verification:** The new row appears in the McpGateway section and the configuration doc builds/preview without broken Markdown.

* [x] **Task 11 — Update production run profile.**
  * Add `downstreamAssemblyHash` under `gateway` **only** in the `production` profile of `deploy/run-profiles.yaml`.
  * Use a placeholder value such as `<replace-with-sha256-of-published-assembly>` and a YAML comment explaining it must be computed from the release image.
  * Do **not** add it to `defaults.gateway`, otherwise Development profiles would inherit an invalid or stale hash.
  * **Verification:** `dotnet run --project src/InfraGate.RunProfiles -- generate production --output /tmp/production.env` emits `InfraGate__Gateway__DownstreamAssemblyHash`, and non-production profiles do not.

* [x] **Task 12 — Update `src/InfraGate.McpGateway/README.md`.**
  * In the "Trusted launch" security control, mention that production deployments must pin the downstream assembly hash via `InfraGate__Gateway__DownstreamAssemblyHash` and that the gateway refuses to start if the DLL is tampered with.
  * Link to `docs/configuration.md` for the env var reference and hash-computation examples.
  * **Verification:** README prose matches the new behavior and the relative link to `docs/configuration.md` resolves.

* [x] **Task 13 — Capture OS-level hardening as a documentation follow-up.**
  * Add a note in the README or `docs/configuration.md` that F-10 hardening item 4 (dedicated OS user, binary signing) is deployment-dependent and not implemented by this change.
  * **Verification:** The note is present and does not claim the gateway enforces OS-level isolation or binary signatures.

### Checkpoint: Implementation complete

* [x] `dotnet build` succeeds with no warnings.
* [x] All modified and new unit tests pass (`rtk test dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`).
* [x] Generated env files from the `production` profile include `InfraGate__Gateway__DownstreamAssemblyHash`.
* [x] Running the gateway in Production with a mismatched or missing hash fails fast with a clear error message.
* [x] Running the gateway in Development without a hash still starts successfully.

## Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `dotnet run --project` cannot be hashed because the DLL is built at runtime | High if we require `DownstreamAssembly` in Production | Production now requires `DownstreamAssembly`; `dotnet run --project` is Development-only. Document this clearly. |
| Existing `McpGatewayOptionsTests` production happy-path tests do not set `DownstreamAssembly` | Test breakage | Update `BuildProductionConfig()` to set `DownstreamAssembly` and `DownstreamAssemblyHash`; inject the fake verifier in `McpGatewayOptionsTests` so no real file is needed. |
| Async verifier inside synchronous `ValidateProductionSafety` | Deadlock in startup | Use a synchronous verifier module; do not change the production-safety contract to async. |
| Hash is computed against the host path instead of the container image path | High (false mismatch in containers) | Document that the hash must be computed against the file inside the runtime image, not the host bind-mount. Provide container-build example. |
| Operator forgets to update hash after upgrading the server image | High (startup failure on legitimate upgrade) | Add the hash computation to the image build/publish pipeline and the run-profile generation docs. |
| Large DLL loaded into memory | Low | Stream the file through `SHA256.Create()`; do not use `File.ReadAllBytes`. |
| Hex comparison is timing-sensitive | Low | Use `CryptographicOperations.FixedTimeEquals` on the byte arrays. |
| Stale default hash inherited by Development profiles | Development startup failures | Put `downstreamAssemblyHash` in the `production` profile only, not in `defaults.gateway`. |
| Opt-in integration/E2E tests that construct `McpGatewayOptions` in `Production` mode | Test breakage | Audit `new McpGatewayOptions(...)` and `FromConfiguration` callers in opt-in test projects; ensure they set `DownstreamAssembly` and a matching hash or run in `Development` mode. |

## Open questions

1. **Require `DownstreamAssembly` in Production?** ✅ **Resolved: yes.** `ValidateProductionSafety` will throw in Production if `DownstreamAssembly` is missing. `dotnet run --project` remains Development-only.

2. **Hash algorithm future-proofing?** ✅ **Resolved: no.** Keep the key as `DownstreamAssemblyHash` and document that it is SHA-256. A new key can be introduced later if needed.

3. **Should the hash be verified on every subprocess spawn or once at startup?** ✅ **Resolved: once at startup is sufficient.** The downstream client is a singleton, so verification in `ValidateProductionSafety` is sufficient and avoids TOCTOU at re-spawn.

## Notes

* Do not edit `.agents/Plans/loose/security-audit.md` as part of this plan. After implementation is verified, a separate update can re-assess F-10 and mark it mitigated.
* The decision to require `DownstreamAssembly` in Production is a behavior change beyond the minimum F-10 mitigation; call it out explicitly in the implementation PR.
* Keep changes surgical: the verifier seam should be one file per top-level type (interface, exception, SHA-256 adapter) under `src/InfraGate.McpGateway/BinaryIntegrity/`, the fake belongs in the test project's `Fakes/` folder, the config change should follow existing `InfraGateGatewaySettings` / `McpGatewayOptions` patterns, and the run-profile change should mirror `DownstreamAssembly`.
