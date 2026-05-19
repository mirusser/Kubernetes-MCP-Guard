# Run Profiles Configuration Migration — Verification Report

Date: 2026-05-16

Full verification of implementation against the plan in `.agents/Plans/loose/2026-05-16-run-profiles-configuration-migration.md` and ADR `docs/adr/0005-use-run-profiles-as-runnable-configuration-source-of-truth.md`.

## Phase 1: Foundation ✅

| Task | Status | Evidence |
|------|--------|----------|
| **1. Add RunProfiles projects** | ✅ DONE | Both projects in `InfraGate.slnx` (lines 6, 16), target `net10.0`, `InternalsVisibleTo` wired |
| **2. Typed YAML model & strict parsing** | ✅ DONE | Unknown keys fail (`ValidateKnownKeys`); duplicate names caught via YamlDotNet exception; unsupported adapter `docker` rejected; exactly-1-adapter enforced. All 5 parser test cases present |
| **3. Canonical `deploy/run-profiles.yaml`** | ✅ DONE | All 10 profiles present; `defaults` section with gateway/identityProvider/approvalAuthority; `domainAdapters` plural; `realmImport` references present (e.g. `deploy/keycloak/infra-gate-realm.json`); no literal secrets |

## Phase 2: Generation ✅

| Task | Status | Evidence |
|------|--------|----------|
| **4. Deterministic env rendering** | ✅ DONE | Header `# Generated from run-profiles.yaml profile: <name>`; 7 ordered sections (Runtime, Gateway, Identity Provider, Approval Authority, Generic Approval Core, Kubernetes Adapter, Host); `KEY=value` lines; no inline comments |
| **5. Exact key-set tests per profile** | ✅ DONE | `ProfileKeySetData` `[Theory]` covers 9 profiles with 3 key-set classes (`ComposeStack`, `SourceGateway`, `Minimal`). `local-stdio` covered by snapshot test |
| **6. Overwrite safety** | ✅ DONE | Foreign files rejected without `--force`; wrong-profile files rejected; matching headers auto-overwrite; `--force` bypasses all checks. 4 dedicated test cases |
| **7. list, validate, generate** | ✅ DONE | All three commands work; `--set`, `--force`, `--output`, `--config` supported. 20 test methods total. Verified: `list` output shows 10 profiles, `validate` returns "valid" |

## Phase 3: Repository Sweep 🟡

| Task | Status | Evidence |
|------|--------|----------|
| **8. Generated output layout + gitignore** | ✅ DONE | `.gitignore` has `deploy/generated/*.env`; `deploy/generated/` directory exists with generated env files; `release.env.example` is committed and NOT gitignored |
| **9. Migrate Compose files** | ✅ DONE | All 4 compose files reference RunProfiles generation in comments; `${VAR}` substitution for runtime values; topology preserved. `compose.release.yaml` uses `--env-file deploy/generated/smoke-release.env` in doc comments |
| **10. Migrate scripts** | 🟡 PARTIAL | `smoke-test-local.sh` ✅, `smoke-test-release.sh` ✅, `setup-development-deploy.sh` ✅ — all auto-generate. **`run-tests.sh`** ❌ — does NOT reference RunProfiles (plan says it should use profile-generated env for test tier toggles) |
| **11. Migrate GitHub Actions** | ✅ DONE | 3 workflows validate profiles: `dotnet-build.yml`, `integration-tests.yml`, `safety-e2e.yml` all run `dotnet run --project src/InfraGate.RunProfiles -- validate` |
| **12. No-SDK quick start** | ✅ DONE | `release.env.example` committed (40 lines); test `ExecuteAsync_GenerateSmokeRelease_MatchesCommittedReleaseExample` verifies byte-for-byte match; Quick start in README uses it |

## Phase 4: Docs ✅

| Task | Status | Evidence |
|------|--------|----------|
| **13. Runnable docs + config reference** | ✅ DONE | `docs/configuration.md` has dedicated **Run Profiles** section (line 67-104); `README.md` shows generation in Option 2; `docs/devs-readme.md` explains generation; `docs/setup-guide.md` references it; `production-oidc.md` preserved untouched |
| **14. RunProfiles documentation** | ✅ DONE | `src/InfraGate.RunProfiles/README.md` (206 lines) covers schema, commands, profile catalogue, `--set` overrides, section opt-in inheritance, output layout, gitignore, secret handling, CI integration |

## Code Standards Check

| Check | Status |
|-------|--------|
| File-scoped namespaces | ✅ All files |
| `sealed` records/classes | ✅ All records and static classes |
| `ConfigureAwait(false)` on all I/O | ✅ `RunProfileCli.cs`, `RunProfileDocumentReader.cs` |
| `const string` for magic strings | ✅ `RunProfileConventions.cs` centralizes all YAML keys, env vars, commands, options |
| `InternalsVisibleTo` for tests | ✅ `InfraGate.RunProfiles.csproj` line 11 |
| Guard helpers (`ThrowIfNull`, etc.) | ✅ |
| One type per file | ✅ |
| Test naming: `Method_State_ExpectedResult` | ✅ e.g. `ExecuteAsync_GenerateWithForceFlag_OverwritesForeignFile` |
| `[Theory]` over duplicated `[Fact]` | ✅ `ProfileKeySetData` theory |
| No `GlobalUsings.cs` | 🟡 Missing per code-standards; csproj-level `<Using>` in test project covers it partially |
| `RunProfileSummary` dead code | 🟡 Defined but never used anywhere |
| `RunProfileDocument` record with behavior | 🟡 Minor - `FindProfileWithDefaults` and merge methods on a `record` |

## Verification Commands

```
dotnet build InfraGate.slnx          → 0 warnings, 0 errors
dotnet test RunProfiles.Tests        → 30 passed, 0 failed, 0 skipped
dotnet run -- ... -- validate        → "Run profile configuration is valid."
dotnet run -- ... -- list            → 10 profiles listed correctly
```

## Remaining Gap

**`scripts/run-tests.sh`** — the plan (Task 10) states it should use "profile-generated env for test tier toggles and kubeconfig defaults." This script currently uses hardcoded env vars and doesn't reference RunProfiles at all. This is the only plan task not fully met.

---

Overall: **13 of 14 tasks complete**, 30 tests passing, build clean, docs aligned. The single gap is `scripts/run-tests.sh` not yet using RunProfiles for its test-tier env.
