# Run Profiles Implementation Gap Analysis

Date: 2026-05-16

Analysis of staged implementation against the plan in `.agents/Plans/loose/2026-05-16-run-profiles-configuration-migration.md` and ADR `docs/adr/0005-use-run-profiles-as-runnable-configuration-source-of-truth.md`.

## Phase 1: Foundation

### Task 1 — Add RunProfiles projects: DONE

- Source and test projects added
- Included in `InfraGate.slnx`
- Both target `net10.0`
- `InternalsVisibleTo` wired

### Task 2 — Typed YAML model and strict parsing: PARTIAL

**Done:**
- Unknown keys fail (`ValidateKnownKeys`)
- Unsupported adapter types fail
- Zero/excess adapters fail

**Missing:**
- No duplicate profile name check — two profiles named `local-stdio` would be silently added twice

### Task 3 — Canonical `deploy/run-profiles.yaml`: PARTIAL

**Done:**
- All 10 agreed profiles present
- `domainAdapters` is plural

**Missing:**
- Missing `defaults` key — the plan explicitly says "Model shared values explicitly with typed `defaults` and per-profile overrides"
- Missing sections: `gateway`, `identityProvider`, `image`, `host` — none exist in the YAML or model types
- Missing Identity Provider realm import references — the plan says profiles reference realm import files, no such field exists

## Phase 2: Generation

### Task 4 — Deterministic env rendering: DONE

- Header names source file, profile, and command
- Fixed section comments
- Deterministic ordering inside each section
- No inline comments after values

### Task 5 — Exact key-set tests per profile: INCOMPLETE

- Only `local-stdio` is tested for generation
- The remaining 9 profiles have no key-set assertions

### Task 6 — Overwrite safety: NOT IMPLEMENTED

- The `generate` command overwrites any file at `--output` unconditionally
- No header-checking (matching generated headers vs. foreign files)
- No wrong-profile detection
- No `--force` flag
- This is the largest gap

### Task 7 — `list`, `validate`, `generate` commands: PARTIAL

**Done:**
- `list` works
- `validate` works (schema only)
- `generate` works (basic path)

**Missing:**
- No `--set path=value` for dynamic overrides
- No `--force` flag
- `validate` doesn't verify repo references (realm import files, kubeconfig paths)

## Phase 3–4: Not Started

No work on repository sweep, Compose migration, scripts, GitHub Actions, no-SDK quick start, or docs.

## Other Gaps

| Severity | Item |
|----------|------|
| Minor | `RunProfileSummary` record is defined but never used (dead code) |
| Minor | `RunProfileDocument` is a `record` with a method (`FindProfile`) — code-standards say records should avoid behavior beyond simple accessors |
| Minor | No `GlobalUsings.cs` — conventions require one per project, though implicit usings + csproj-level `<Using>` in the test project covers this partially |
| Minor | `version` field in YAML is recognized but never validated (e.g., unknown version numbers pass silently) |

## What is Well-Aligned

- File-scoped namespaces
- `sealed` records
- `ConfigureAwait(false)` on all I/O
- `const string` conventions for magic strings in `RunProfileConventions`
- Guard helpers (`ThrowIfNull`, `ThrowIfNullOrEmpty`)
- One type per file
- Test naming (`ExecuteAsync_List_PrintsProfiles`)

## Summary

The implementation matches Phase 1 (Tasks 1–3) at ~70% and Phase 2 (Tasks 4–7) at ~40%. The biggest missing pieces are: overwrite safety (Task 6 entirely), the `defaults` layer, the extra domain sections (`gateway`, `identityProvider`, `image`, `host`), `--set`/`--force` flags, and comprehensive profile generation tests.
