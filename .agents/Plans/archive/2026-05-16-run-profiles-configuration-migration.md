# Run Profiles Configuration Migration Plan

Date: 2026-05-16

## Grilling Decisions

- A typed **Run Profile** is the human-edited authoring source of truth for runnable configuration.
- Environment variables remain the runtime transport for Docker, CI, scripts, and .NET processes.
- A **Run Profile** describes target runnable environment shape only. GitHub Actions keeps ownership of triggers, job ordering, credentials, image publishing policy, and branch/tag rules.
- The canonical profile file is YAML, not TOML.
- Use one canonical file: `deploy/run-profiles.yaml`.
- The first generated artifact type is `.env` files for Compose, scripts, tests, and workflows. Do not generate Compose YAML in this migration.
- Generated `.env` files are build artifacts and stay gitignored.
- Commit only one no-SDK release/demo env example for the published-image quick start; CI must verify it against the profile.
- Secrets are not stored in YAML. The profile may reference external env/secret names and generation fails for missing required values when materializing a deployable profile.
- Include local developer run profiles as well as container and CI profiles. Initial profile set:
  - `local-compose`
  - `local-source-gateway`
  - `local-stdio`
  - `development`
  - `production`
  - `test-integration`
  - `test-gateway-integration`
  - `test-safety-e2e`
  - `smoke-local`
  - `smoke-release`
- Implement the generator as a .NET repo tool: `InfraGate.RunProfiles`.
- `InfraGate.RunProfiles` is not a shipping runtime artifact and is not copied into gateway images.
- Validation is layered: profile authoring/deploy-time checks in the tool, existing runtime safety checks stay as final startup gates.
- Full repository-wide sweep is desired: source, tests, scripts, workflows, Compose, Docker-adjacent run config, and docs.
- Scripts/workflows that consume generated env files should auto-generate them before use.
- Preserve no-.NET SDK published-image quick start through the committed release/demo env example.
- Model shared values explicitly with typed `defaults` and per-profile overrides. Do not use YAML anchors as the main abstraction.
- YAML keys are domain-shaped and should align with `CONTEXT.md` where concepts exist.
- Top-level vocabulary:
  - `version`
  - `defaults`
  - `profiles`
  - sections such as `gateway`, `identityProvider`, `approvalAuthority`, `genericApprovalCore`, `domainAdapters`, `image`, and `host`
- `domainAdapters` is plural now, but the first implementation supports exactly one adapter of type `kubernetes`.
- The Run Profile references Identity Provider realm import files; it does not inline Keycloak users, clients, scopes, mappers, or DCR policies.
- Docker network topology stays in Compose, but the profile declares required network names and endpoint addresses that affect generated env.
- MCP client config is out of scope. Profiles may hold MCP endpoint/resource/scope values, but the tool must not write user MCP client config.
- Keep existing runtime env variable names, including `K8S_MCP_*`, for this migration.
- Unknown YAML keys are strict errors.
- Generated env files include a generated header, section comments, and deterministic `KEY=value` lines with no inline comments.
- Generation overwrites only files with a matching generated header unless `--force` is supplied.
- `setup-development-deploy.sh` delegates env creation to `InfraGate.RunProfiles` but still owns host preparation, kubeconfig copy, local Keycloak startup, and reachability checks.
- Dynamic discovered values are CLI overrides to the generator, not writes back to `deploy/run-profiles.yaml`.
- Tool commands: `list`, `validate`, `generate`.
- Validation has levels: default schema/repo reference validation, materialized profile validation during generation.
- Generated env files include only variables required by the runnable path.
- Tests must assert exact generated env key sets per profile.
- Generated env output is grouped in fixed human-oriented sections with deterministic ordering inside each section.

## Architecture Decisions

- Add a repo-tool **Module**: `InfraGate.RunProfiles`.
- Its **Interface** is small: `list`, `validate`, and `generate`, plus the typed `deploy/run-profiles.yaml` schema.
- Its **Implementation** hides YAML parsing, defaults and overrides, env variable mapping, safety checks, materialized path checks, overwrite protection, and env rendering.
- The **Seam** is deep when callers only need a profile name and output path, rather than knowing every env var and default.
- The module should improve **Locality** by concentrating runnable configuration rules in one place.
- The module should provide **Leverage** by serving Compose, scripts, workflows, tests, and docs from one authoring source.

## Implementation Plan

### Phase 1: Foundation

#### Task 1: Add RunProfiles projects

**Description:** Add `src/InfraGate.RunProfiles` as a .NET console repo tool and `tests/InfraGate.RunProfiles.Tests` as its test project.

**Acceptance criteria:**
- Console and test projects are included in `InfraGate.slnx`.
- Projects target `net10.0` and match repo project style.
- Tests can call the public tool interface without testing private methods.

**Verification:**
- `dotnet build InfraGate.slnx`

**Dependencies:** None

**Files likely touched:**
- `InfraGate.slnx`
- `src/InfraGate.RunProfiles/InfraGate.RunProfiles.csproj`
- `tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`

**Estimated scope:** Medium

#### Task 2: Define typed YAML model and strict parsing

**Description:** Implement strict YAML parsing for the domain-shaped Run Profile schema.

**Acceptance criteria:**
- Unknown YAML keys fail.
- Duplicate profile names fail.
- Missing or unsupported `domainAdapters` fail.
- The only supported adapter type is `kubernetes`.

**Verification:**
- Parser tests for valid config, unknown key, duplicate profile, zero adapters, duplicate adapter names, and unsupported adapter.

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/UnitTests/*`

**Estimated scope:** Medium

#### Task 3: Add canonical `deploy/run-profiles.yaml`

**Description:** Add the canonical profile file with the initial profile set and one Kubernetes domain adapter.

**Acceptance criteria:**
- All agreed profiles exist.
- `domainAdapters` is plural.
- Local/demo Keycloak profiles reference the existing realm import file.
- No secrets are stored as literal secret values.

**Verification:**
- `dotnet run --project src/InfraGate.RunProfiles -- validate`

**Dependencies:** Task 2

**Files likely touched:**
- `deploy/run-profiles.yaml`

**Estimated scope:** Medium

### Checkpoint 1

- `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- `dotnet build InfraGate.slnx`

### Phase 2: Generation

#### Task 4: Implement deterministic env rendering

**Description:** Generate `.env` output with a header, fixed section comments, deterministic ordering, and `KEY=value` lines.

**Acceptance criteria:**
- Header names source file, profile, and command.
- No inline comments after values.
- Output is deterministic.

**Verification:**
- Snapshot-style tests assert full output for a simple profile.

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/UnitTests/*`

**Estimated scope:** Medium

#### Task 5: Assert exact generated key sets per profile

**Description:** Add tests that assert each runnable profile emits only the env variables it needs.

**Acceptance criteria:**
- Every agreed profile has an exact key-set test.
- No all-purpose env dump exists.

**Verification:**
- `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`

**Dependencies:** Task 4

**Files likely touched:**
- `tests/InfraGate.RunProfiles.Tests/UnitTests/*`

**Estimated scope:** Medium

#### Task 6: Implement overwrite safety

**Description:** Protect hand-written env files from accidental overwrite.

**Acceptance criteria:**
- Matching generated headers can be overwritten automatically.
- Foreign files fail unless `--force` is supplied.
- Wrong-profile generated files fail unless `--force` is supplied.

**Verification:**
- Temp-file tests for generated, foreign, wrong-profile, and forced overwrites.

**Dependencies:** Task 4

**Files likely touched:**
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/UnitTests/*`

**Estimated scope:** Medium

#### Task 7: Implement `list`, `validate`, and `generate`

**Description:** Provide the CLI commands needed by humans, scripts, and CI.

**Acceptance criteria:**
- `list` shows available profiles and kinds.
- `validate` checks schema and repo references by default.
- `generate` validates, materializes a profile, and writes env output.
- `--set path=value`, `--force`, and `--output` are supported for generation.

**Verification:**
- CLI behavior tests using temp directories.
- Manual commands:
  - `dotnet run --project src/InfraGate.RunProfiles -- list`
  - `dotnet run --project src/InfraGate.RunProfiles -- validate`

**Dependencies:** Tasks 2, 4, 6

**Files likely touched:**
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/UnitTests/*`

**Estimated scope:** Medium

### Checkpoint 2

- `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- `dotnet run --project src/InfraGate.RunProfiles -- list`
- `dotnet run --project src/InfraGate.RunProfiles -- validate`

### Phase 3: Repository Sweep

#### Task 8: Update generated output layout and gitignore

**Description:** Add `deploy/generated/*.env` as the generated env location and keep it out of git.

**Acceptance criteria:**
- Generated env files are ignored.
- The no-SDK release/demo env example is not ignored.

**Verification:**
- `git check-ignore deploy/generated/local-compose.env`

**Dependencies:** Task 7

**Files likely touched:**
- `.gitignore`

**Estimated scope:** Small

#### Task 9: Migrate Compose files to generated env inputs

**Description:** Remove duplicated runtime defaults from Compose files where generated env owns the value while keeping service topology in Compose.

**Acceptance criteria:**
- Compose service/network/volume topology remains in Compose.
- Runtime values come from generated env files.
- Local build/release/development/production Compose paths still render with `docker compose config`.

**Verification:**
- `docker compose --env-file deploy/generated/local-compose.env -f deploy/local-oauth/compose.yaml config`
- Equivalent config checks for release, development, and production profiles.

**Dependencies:** Tasks 7, 8

**Files likely touched:**
- `deploy/local-oauth/compose.yaml`
- `deploy/local-oauth/compose.release.yaml`
- `deploy/compose/development.yaml`
- `deploy/compose/production.yaml`
- `deploy/compose/keycloak.yaml`

**Estimated scope:** Medium

#### Task 10: Migrate scripts to auto-generate env

**Description:** Update runnable scripts to generate their required env file before use.

**Acceptance criteria:**
- Smoke scripts generate their profile env before Compose.
- `run-tests.sh` uses profile-generated env for test tier toggles and kubeconfig defaults.
- `setup-development-deploy.sh` delegates `/etc/infra-gate/development.env` creation to the tool and passes discovered values as CLI overrides.
- Bootstrap/kubeconfig scripts do not duplicate values that now live in the profile unless the value is a local operational constant.

**Verification:**
- Script help/dry-run checks where available.
- Focused smoke/profile generation commands.

**Dependencies:** Tasks 7, 8

**Files likely touched:**
- `scripts/smoke-test-local.sh`
- `scripts/smoke-test-release.sh`
- `scripts/run-tests.sh`
- `scripts/setup-development-deploy.sh`
- `scripts/create-demo-kubeconfig.sh`

**Estimated scope:** Large, split further during implementation if needed

#### Task 11: Migrate GitHub Actions

**Description:** Update workflows to validate/generate profile env before test and deploy steps.

**Acceptance criteria:**
- CI policy remains in workflow YAML.
- Test/deploy runtime settings come from generated env where applicable.
- The no-SDK release/demo env example is validated in CI.

**Verification:**
- YAML review plus local commands used by workflows.
- Full proof occurs in CI.

**Dependencies:** Tasks 7, 8

**Files likely touched:**
- `.github/workflows/*.yml`

**Estimated scope:** Medium

#### Task 12: Preserve no-SDK published-image quick start

**Description:** Add one committed release/demo env example for users who run published images without the .NET SDK.

**Acceptance criteria:**
- The example is generated-equivalent and committed.
- CI/test command verifies it matches the profile output.
- Quick start docs use it.

**Verification:**
- RunProfiles verification test comparing the committed example to generated output.

**Dependencies:** Tasks 4, 7

**Files likely touched:**
- `deploy/local-oauth/release.env.example`
- `tests/InfraGate.RunProfiles.Tests/*`

**Estimated scope:** Small

### Checkpoint 3

- `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`
- Compose config checks for all Compose-backed profiles.
- Generated env files can be recreated but are not tracked.

### Phase 4: Docs

#### Task 13: Update runnable docs and configuration reference

**Description:** Make docs point to Run Profiles as source of truth and remove hand-maintained env blocks where they conflict with that.

**Acceptance criteria:**
- Runnable docs explain automatic generation.
- `docs/configuration.md` describes generated env transport and the profile source.
- Production OIDC docs preserve external provider guidance.
- No-SDK quick start is documented with the committed release/demo env example.

**Verification:**
- Command snippets match actual scripts and profiles.

**Dependencies:** Phase 3

**Files likely touched:**
- `README.md`
- `docs/devs-readme.md`
- `docs/setup-guide.md`
- `docs/configuration.md`
- `docs/production-oidc.md`
- relevant project READMEs

**Estimated scope:** Large, split further during implementation if needed

#### Task 14: Add RunProfiles documentation

**Description:** Document the profile schema, commands, output layout, secret handling, override rules, and profile list.

**Acceptance criteria:**
- Humans can discover how to list, validate, and generate profiles.
- The doc explains what the profile owns and what CI/runtime still owns.

**Verification:**
- Examples run successfully.

**Dependencies:** Tasks 7, 13

**Files likely touched:**
- `src/InfraGate.RunProfiles/README.md` or `docs/run-profiles.md`

**Estimated scope:** Medium

### Final Verification

- `dotnet build InfraGate.slnx`
- `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`
- `dotnet run --project src/InfraGate.RunProfiles -- validate`
- Generate every profile and assert no generated env file is tracked.
- Run Compose `config` for each Compose-backed profile.

## Risks And Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Full sweep touches many files | High | Implement in vertical TDD slices with checkpoints. |
| YAML parser strictness becomes too hard to evolve | Medium | Version the schema and keep errors clear. |
| Generated env files hide missing materialized host state | Medium | Split schema validation from generation/materialized validation. |
| No-SDK quick start drifts from profile | Medium | Commit one example and verify it in tests/CI. |
| Runtime safety checks get weakened | High | Keep existing runtime checks and add profile checks as an earlier gate. |
| Env rename temptation expands scope | High | Keep existing env names; consider separate ADR later. |

## TDD Starting Slice

Start with one behavior through the public CLI surface:

1. RED: `list` over a minimal YAML profile prints available profiles and returns exit code `0`.
2. GREEN: Add minimal project, parser, and command implementation to pass.
3. Repeat vertically for `validate`, strict unknown keys, and `generate` deterministic env output.

