# Implementation Plan: Appsettings-First Configuration with a Single `.env` per Profile

> Status: proposed (2026-06-02). No code changed yet. Branch: work on the current branch, **do not commit** until reviewed.

## Overview

Collapse InfraGate's configuration fan-out (one setting hand-declared in ~14 places across 4 naming
conventions) down to the idiomatic .NET model used by `Simple-Weather-Site/CitiesService`:

- Bind typed settings with the stock binder: `services.Configure<T>(config.GetSection(section))`.
- Drive containers from **one** machine-generated `.env` per profile, using the framework `__`
  hierarchy convention (`InfraGate__Kubernetes__AllowedNamespaces__0`), consumed by Compose via
  `--env-file` + service `env_file:` (the large `environment:` passthrough block is deleted).
- Keep `run-profiles.yaml` as the human source of truth (readable camelCase); the `.env` is a
  gitignored, do-not-edit artifact. `appsettings.json` per service holds static, non-secret defaults.
- Delete the custom env-mapping machinery: `InfraGateEnvironmentVariablesConfigurationProvider`,
  `…Source`, `…Extensions`, `InfraGateEnvVarMappings`, every `RegisterInfraGateEnvVarMappings`, the
  per-service `EnvironmentVariables`/`ConfigurationKeys` field tables, the dual
  `FromEnvironment`/`FromConfiguration` parsers, and the RunProfiles `AppSettingsRenderer`.

Net effect: adding a setting becomes a 2-place change (YAML + POCO) instead of ~14; roughly 1,500+
lines of conventions/mapping/renderer glue are removed.

## Architecture Decisions

- **`__` env naming is forced, and that's fine.** Single-`.env` + zero mapping implies the framework
  `__` convention (anything else needs a translation layer — the thing we are deleting). The `__`
  keys only ever appear in generated artifacts (the `.env`, k8s ConfigMaps/Secrets) and ad-hoc
  overrides; humans author the YAML. This matches CitiesService's k8s overlay env files.
- **Section binding via `Configure<T>(GetSection)`.** Each service keeps a bindable settings type
  (`sealed class`, `get; set;`/`init` properties — the binder needs settable members), bound from a
  named section under `InfraGate:`. Section-root strings stay as constants (code-standards: no magic
  strings); the **per-field** env/key constant tables are deleted.
- **Validation moves to `IValidateOptions<T>` + `ValidateOnStart()`.** The current manual
  `ValidateProductionSafety()` calls in `Program.cs` become `sealed` validators registered via
  `services.AddOptions<T>().Bind(section).ValidateOnStart()`. `ProductionSafetyValidator`,
  `RuntimeMode`, and `RuntimeModeResolver` logic is preserved (moved, not dropped).
- **Downstream auth is secure-by-default.** `DownstreamAuthOptions.Required` and
  `RequireHttpsMetadata` default to `true` — an absent key means **on**. Local/dev run profiles MUST
  explicitly turn it **off** (`downstreamAuth.required: "false"`); `production` needs no section (it
  inherits the secure default). The POCO-default flip happens in **Phase 2** (not the behavior-
  preserving Phase 1), together with an audit of every `new DownstreamAuthOptions()` call-site —
  especially the gateway's `?? new DownstreamAuthOptions()` null-provider fallback in
  `ConfigurationExtensions.cs` — so an unconfigured instance stays safe rather than silently demanding
  auth with empty credentials.
- **`appsettings.json` = static defaults only.** Today only McpServer and McpGateway ship one (logging
  defaults). Observer/Planner/Executor get an `appsettings.json` added. Per-environment files
  (`appsettings.Development.json`, etc.) are optional and selected by `DOTNET_ENVIRONMENT`.
- **The single `.env` mixes two logical groups in one file:** app config in `__` form
  (`InfraGate__…`, consumed by the app via the stock env provider) and Compose orchestration vars in
  plain form (`INFRA_GATE_BIND_PORT`, host paths, image — consumed by Compose `${…}` interpolation).
  Each side ignores the other's keys; both are passed into the container by `env_file:` harmlessly.
- **Secrets live in the gitignored generated `.env`** (e.g. `deploy/generated/<profile>.env`),
  injected at generation time by `scripts/generate-env.sh` from the operator's shell/secret store, in
  `__` form so they bind to their section. `run-profiles.yaml` stays secret-free. (Confirm injection
  source — see Open Questions.)
- **Allowlist posture preserved if required.** Removing the custom provider means the stock
  `AddEnvironmentVariables()` reads all vars into config. If the "only InfraGate-prefixed vars are
  honored" property must hold, use `AddEnvironmentVariables(prefix: "InfraGate__")` (binds only
  prefixed vars; note the prefix is stripped, so section roots must account for it). Decide in Task 8.
- **Transition stays green because the stock env provider is already in the pipeline.** Both
  `Program.cs` files already call `AddEnvironmentVariables()` after the custom provider, so `__` keys
  already bind today. A migrated service reads its section regardless of whether the value arrived via
  the old custom name (custom provider → section) or a `__` key (stock provider → section). The custom
  provider and the old-named `.env` are therefore removed **last** (Task 8), after every service binds
  sections — nothing breaks mid-flight.
- **Tests use in-memory `IConfiguration`, never mocks** (writing-tests skill). Migrate
  `FromEnvironment()` call-sites to `ConfigurationBuilder().AddInMemoryCollection(...)` +
  section binding. No Moq/NSubstitute.

## Dependency Graph

```
Task 1  parser collapse (FromEnvironment → shim over FromConfiguration)   [foundation]
   │
Task 2  McpServer → section binding + IValidateOptions + ValidateOnStart  [pilot, resolves stdio]
   │        (add appsettings defaults; migrate McpServer.Tests off FromEnvironment)
   │
   ├── Task 3  McpGateway → section binding (+ migrate McpGateway.Tests)
   ├── Task 4  Observer   → section binding (+ add appsettings.json)
   ├── Task 5  Planner    → section binding (+ add appsettings.json)
   └── Task 6  Executor   → section binding (+ add appsettings.json)
   │        (Tasks 3–6 are independent of each other; safe to parallelize)
   │
Task 7  RunProfiles: render single `__` .env; delete AppSettingsRenderer (+ rewrite renderer tests)
   │
Task 8  Delete custom provider + InfraGateEnvVarMappings + all Register…; Program.cs config = AddJsonFile + AddEnvironmentVariables + AddCommandLine
   │
Task 9  Rewrite Compose to one env_file; regenerate committed release.* examples
   │
Task 10 Docs: rewrite docs/configuration.md, service READMEs, RunProfiles README, devs-readme, onboarding skill; ADR 0030
```

Implementation order is bottom-up; high-risk items (stdio in Task 2, deploy in Tasks 7–9) are early
within their phase to fail fast.

---

## Task List

### Phase 1: Foundation — one parser per option type

#### Task 1: Collapse `FromEnvironment` into a shim over `FromConfiguration`

**Description:** For each options type with both factories (`KubernetesMcpOptions`,
`McpGatewayOptions`, `GatewayAuthOptions`, `DownstreamAuthOptions`, `RuntimeModeResolver`), make
`FromEnvironment()` build an in-memory/stock `IConfiguration` from environment variables and delegate
to `FromConfiguration(IConfiguration)`. Eliminate the duplicated parsing body so there is a single
parse path. No behavior change, no naming change yet.

**Acceptance criteria:**
- [ ] Each `FromEnvironment()` contains no bespoke parsing — it constructs config and calls `FromConfiguration`.
- [ ] No duplicated field-parsing logic remains across the two factories of any one type.
- [ ] Public/observable behavior is unchanged (same values produced for the same env).

**Verification:**
- [ ] Build clean (warnings-as-errors): `dotnet build`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.DownstreamAuth.Tests/InfraGate.DownstreamAuth.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpServer/Configuration/KubernetesMcpOptions.cs`
- `src/InfraGate.McpGateway/Configuration/McpGatewayOptions.cs`, `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs`
- `src/InfraGate.DownstreamAuth/DownstreamAuthOptions.cs`, `src/InfraGate.RuntimeSafety/RuntimeModeResolver.cs`

**Estimated scope:** Medium (3–5 files)

**APPROACH CORRECTION (2026-06-02).** The original "Phase 1 = collapse `FromEnvironment` into a manual
`FromConfiguration`" step was wrong — it still read config **per key** (`configuration[key]`), which is
the antipattern, not the CitiesService model. That attempt on `DownstreamAuthOptions` was **reverted**.

Verified against official docs (Microsoft Learn, *Options pattern in .NET*): the binder *"matches
property names against configuration keys recursively"* — bind a whole section to a POCO and nested
objects bind automatically; never per-key. Canonical form:
`services.AddOptions<T>().Bind(config.GetSection(SectionName)).Validate(...).ValidateOnStart()` (or
`services.Configure<T>(config.GetSection(SectionName))`), consumed via `IOptions<T>`. Confirmed in
Simple-Weather (`EmailService`, `ApiResourceAuthSettings` nested binding).

**Revised strategy: vertical slices per service** (one options class per project bound to its section,
no per-key reads, no `mappings.Map`), instead of horizontal parser-collapse. The custom provider stays
until all services are migrated, then is deleted with the `__` transport flip.

✅ **Executor slice DONE & verified** (build 0/0, **94 Executor tests pass**, `RunProfiles validate`
clean): `ExecutorOptions` now binds the entire `InfraGate:Executor` section (added OAuth fields);
`Program.cs` dropped the `AddInfraGateEnvironmentVariables`/`mappings.Map` block and all per-key OAuth/URL
reads; `ExecutorConventions` env/key tables replaced by a single `SectionName`; added
`appsettings.json`; compose `executor` service now passes `InfraGate__Executor__*` keys (stock binder).
Remaining services: McpServer, McpGateway, Observer, Planner (+ shared `DownstreamAuthOptions` as a
nested bound section). `DownstreamAuthOptions` still carries the secure-default coupling (set
`Required=true` POCO default when it's migrated; dev profiles `required=false`).

### Checkpoint: Foundation
- [ ] All four test projects above pass; build is clean.
- [ ] No env-var names or config keys changed yet (pure de-duplication).

---

### Phase 2: Pilot — McpServer end-to-end (resolves the stdio risk early)

#### Task 2: Migrate McpServer to section binding + options validation

**Description:** Bind `InfraGate:Kubernetes` (and downstream-auth section) via
`services.AddOptions<InfraGateKubernetesSettings>().Bind(section).ValidateOnStart()` with a `sealed`
`IValidateOptions<InfraGateKubernetesSettings>` carrying the `ValidateProductionSafety` logic. Build
the runtime `KubernetesMcpOptions` from the bound settings (no raw-key reads). Confirm the stdio path:
the server already reads `INFRA_GATE_CONFIG_PATH` JSON + stock env, so it must work from a generated
appsettings/`__` env without the custom provider's old names (decide deprecation shim — Open
Questions). Migrate `K8SMcpOptionsTests` off `FromEnvironment()` to in-memory `IConfiguration`.

**Acceptance criteria:**
- [ ] McpServer resolves Kubernetes + downstream-auth config purely from bound sections; no per-field raw env reads in `Program.cs`.
- [ ] Production-safety rules run via `ValidateOnStart()` and fail startup with the same messages as before.
- [ ] stdio launch produces identical allowed-namespaces/kubeconfig behavior (verified via the integration path).
- [ ] `K8SMcpOptionsTests` use in-memory `IConfiguration` (no `FromEnvironment`, no mocks).
- [ ] `DownstreamAuthOptions.Required`/`RequireHttpsMetadata` POCO defaults set to `true` (secure-by-default), and every `new DownstreamAuthOptions()` call-site audited (esp. the gateway null-provider fallback) so an unconfigured instance stays safe.

**Verification:**
- [ ] `dotnet build`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] Manual stdio check: launch `local-stdio` per `docs/devs-readme.md` and confirm `get_allowed_namespaces` returns the configured set.

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpServer/Program.cs`, `src/InfraGate.McpServer/Configuration/KubernetesMcpOptions.cs`
- `src/InfraGate.McpServer/Configuration/InfraGateKubernetesSettings.cs` (+ new validator file)
- `src/InfraGate.McpServer/appsettings.json` (defaults if needed)
- `tests/InfraGate.McpServer.Tests/UnitTests/K8SMcpOptionsTests.cs`

**Estimated scope:** Medium (3–5 files)

### Checkpoint: Pilot
- [ ] McpServer unit tests pass; build clean.
- [ ] stdio behaves identically (the only externally-launched surface) — **review with human before rollout.**

---

### Phase 3: Roll out section binding to the remaining services

> Tasks 3–6 are independent and may be parallelized. Each removes that service's field-level
> `EnvironmentVariables`/`ConfigurationKeys` usage from `Program.cs`/registration in favour of
> `Configure<T>(GetSection)` + a validator, and migrates that service's tests off `FromEnvironment()`.

#### Task 3: McpGateway → section binding + validation
**Description:** Replace `McpGatewayOptions.FromConfiguration(...)` raw-key construction with bound
settings (`InfraGate:Gateway`/`:Auth`/`:Approval` already bound — extend to all gateway config) +
`IValidateOptions`. Migrate the ~30 `McpGatewayOptions.FromEnvironment()` and `GatewayAuthOptions`
test call-sites to in-memory `IConfiguration`.
**Acceptance criteria:** [ ] gateway config comes from bound sections; [ ] production validation via `ValidateOnStart`; [ ] gateway tests use in-memory config, no mocks.
**Verification:** [ ] `dotnet build`; [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
**Dependencies:** Task 2 (pattern established)
**Files likely touched:** `src/InfraGate.McpGateway/Configuration/ConfigurationExtensions.cs`, `…/McpGatewayOptions.cs`, `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs` (+ validator), `tests/InfraGate.McpGateway.Tests/UnitTests/McpGatewayOptionsTests.cs`, `…/GatewayAuthOptionsTests.cs`
**Estimated scope:** Medium–Large (verify ≤5; split auth into its own task if it grows)

#### Task 4: Observer → section binding (+ add `appsettings.json`)
**Description:** Replace the inline `mappings.Map(...)` block in `Observer/Program.cs:31-47` with
`Configure<ObserverOptions>(GetSection)` (already partly present) + validator; add a committed
`appsettings.json` (logging + static defaults).
**Acceptance criteria:** [ ] no inline env→key mapping in `Program.cs`; [ ] Observer binds from section; [ ] appsettings.json added.
**Verification:** [ ] `dotnet build`; [ ] `dotnet test tests/InfraGate.Observer.Tests/...` (if present; otherwise build + manual)
**Dependencies:** Task 2
**Files likely touched:** `src/InfraGate.Observer/Program.cs`, `…/ObserverOptions.cs`, `…/ObserverConventions.cs` (shrink), new `src/InfraGate.Observer/appsettings.json`
**Estimated scope:** Medium

#### Task 5: Planner → section binding (+ add `appsettings.json`)
Mirror Task 4 for Planner. **Files:** `src/InfraGate.Planner/Program.cs`, `…/PlannerOptions.cs`, `…/PlannerConventions.cs`, new `appsettings.json`. **Verification:** `dotnet build` + Planner tests. **Dependencies:** Task 2. **Scope:** Medium

#### Task 6: Executor → section binding (+ add `appsettings.json`)
Mirror Task 4 for Executor. **Files:** `src/InfraGate.Executor/Program.cs`, `…/ExecutorOptions.cs`, `…/ExecutorConventions.cs`, new `appsettings.json`. **Verification:** `dotnet build` + Executor tests. **Dependencies:** Task 2. **Scope:** Medium

### Checkpoint: All services on section binding
- [ ] Every service binds via `Configure<T>(GetSection)` + `ValidateOnStart`; no service reads per-field raw env keys.
- [ ] All service test projects pass; build clean.
- [ ] The custom provider is still present (fed by old-named `.env`) — system fully green.

---

### Phase 4: Switch the transport and delete the mapping layer

#### Task 7: RunProfiles renders a single `__` `.env`; delete `AppSettingsRenderer`

**Description:** Change `EnvFileRenderer` to emit app config as `InfraGate__Section__Key` (lists as
`…__0/__1`) alongside the existing Compose orchestration vars in one file. Delete `AppSettingsRenderer`
and its `--format appsettings` path (containers no longer consume a generated appsettings). Shrink
`RunProfileConventions` to section roots + orchestration var names. Update/replace
`EnvFileRendererTests`; delete `AppSettingsRendererTests`.

**Acceptance criteria:**
- [ ] `generate <profile>` produces one `.env` whose app keys are `__`-form and bind to the sections used in Phase 2–3.
- [ ] `--format appsettings` is removed (or returns a clear error) and `AppSettingsRenderer` is deleted.
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes for all profiles.
- [ ] Local/dev profiles (`local-source-gateway`, `local-stdio`, `development`, `test-integration`, `test-gateway-integration`, `test-safety-e2e`) explicitly emit downstream-auth `required=false`; `local-compose`/`smoke-*` keep `required=true`; `production` keeps no section (inherits the secure `true` default).

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `./scripts/generate-env.sh local-compose` then inspect `deploy/generated/local-compose.env` for `__` keys.

**Dependencies:** Tasks 2–6 (services must read sections first)
**Files likely touched:** `src/InfraGate.RunProfiles/Rendering/EnvFileRenderer.cs`, delete `…/AppSettingsRenderer.cs`, `…/RunProfileConventions.cs`, `…/Cli/RunProfileCli.cs`, `tests/InfraGate.RunProfiles.Tests/UnitTests/EnvFileRendererTests.cs` (rewrite), delete `…/AppSettingsRendererTests.cs`
**Estimated scope:** Medium–Large (verify ≤5; the renderer + its tests + conventions are tightly coupled)

#### Task 8: Delete the custom env provider and all mapping glue

**Description:** Remove `InfraGateEnvironmentVariablesConfigurationProvider`, `…Source`,
`…Extensions`, `InfraGateEnvVarMappings`, and every `RegisterInfraGateEnvVarMappings`. Replace each
service's `AddInfraGateConfiguration` with `AddJsonFile(configPath) + AddEnvironmentVariables() +
AddCommandLine(args)`. Decide and apply the allowlist prefix (`AddEnvironmentVariables("InfraGate__")`)
if the security posture requires it. Keep `RuntimeMode`/`RuntimeModeResolver`/`ProductionSafetyValidator`.

**Acceptance criteria:**
- [ ] The four mapping types/files are deleted; no `RegisterInfraGateEnvVarMappings` remains (`grep` clean).
- [ ] Each `Program.cs` builds config with stock providers only.
- [ ] Allowlist decision recorded (prefix on/off) and reflected in code + ADR.

**Verification:**
- [ ] `dotnet build` (whole solution)
- [ ] `dotnet test` (full suite)
- [ ] `grep -rn 'RegisterInfraGateEnvVarMappings\|InfraGateEnvVarMappings' src` returns nothing.

**Dependencies:** Task 7
**Files likely touched:** delete 4 files in `src/InfraGate.RuntimeSafety/`; edit `Program.cs`/`ConfigurationExtensions.cs` across McpServer, McpGateway, Observer, Planner, Executor (+ remove their `…Conventions` env tables)
**Estimated scope:** Large — **split per service if it exceeds ~5 files** (one task to delete RuntimeSafety pieces once callers are gone, plus one small task per service to swap `Program.cs`).

#### Task 9: One `env_file` in Compose; regenerate committed release examples

**Description:** Rewrite `deploy/local-oauth/compose.yaml` and `compose.release.yaml` (and
`deploy/compose/{development,production}.yaml`) so each service uses a single `env_file:` and the large
`environment:` passthrough block is removed. Regenerate `deploy/local-oauth/release.env.example` (now
`__`-form) and delete `release.appsettings.json`. Update `scripts/generate-env.sh` for the single-file
output (+ secret injection).

**Acceptance criteria:**
- [ ] Each Compose service is configured by one `env_file`; no per-key `environment:` passthrough for app config.
- [ ] Committed `release.env.example` regenerated; obsolete `release.appsettings.json` removed.
- [ ] Local stack starts and the smoke test passes.

**Verification:**
- [ ] `./scripts/smoke-test-local.sh` (local-build stack)
- [ ] `docker compose --env-file deploy/generated/local-compose.env -f deploy/local-oauth/compose.yaml config` parses.

**Dependencies:** Tasks 7–8
**Files likely touched:** `deploy/local-oauth/compose.yaml`, `…/compose.release.yaml`, `deploy/compose/development.yaml`, `…/production.yaml`, `scripts/generate-env.sh`, committed `deploy/local-oauth/release.env.example` (delete `release.appsettings.json`)
**Estimated scope:** Medium–Large (deploy-only; verify the smoke test gates it)

### Checkpoint: Transport switched
- [ ] Full `dotnet build` + `dotnet test` green; profiles `validate` clean.
- [ ] `smoke-test-local` passes against the single-`.env` Compose stack.
- [ ] No mapping glue remains in `src`. **Review with human before docs phase.**

---

### Phase 5: Documentation and ADR

#### Task 10: Update canonical docs + ADR 0030

**Description:** Per verify-readme-docs, treat code/tests as source of truth and patch real drift.
Rewrite the env-var sections of `docs/configuration.md` (canonical reference; per-service sections
McpServer/Observer/Planner/Executor/McpGateway/Auth + Run Profiles + CI/Release) to the `__` +
section model. Update each `src/*/README.md` config note, `src/InfraGate.RunProfiles/README.md`
(single `.env`, no appsettings format), `docs/devs-readme.md` run commands, and the
`.agents/skills/repo-onboarding/SKILL.md` config-table row. Add `docs/adr/0030-appsettings-first-single-env-configuration.md`.

**Acceptance criteria:**
- [ ] `docs/configuration.md` reflects actual env names/sections/defaults (no stale `INFRA_GATE_*`/`K8S_MCP_*` app-config names; no aspirational claims).
- [ ] RunProfiles README documents single-`.env` output and the removed `appsettings` format.
- [ ] ADR 0030 records the decision, the `__`-naming consequence, the stdio contract change, and the allowlist choice.

**Verification:**
- [ ] `git diff --check` (whitespace)
- [ ] `rg -n 'INFRA_GATE_|K8S_MCP_' docs README.md src/*/README.md` shows only intentional (orchestration/secret) references.
- [ ] Links resolve: `rg -n 'configuration.md' README.md docs src/*/README.md`.

**Dependencies:** Tasks 1–9
**Files likely touched:** `docs/configuration.md`, `docs/devs-readme.md`, `src/InfraGate.RunProfiles/README.md`, affected `src/*/README.md`, `.agents/skills/repo-onboarding/SKILL.md`, new `docs/adr/0030-*.md`
**Estimated scope:** Large — **split** into (10a) `configuration.md` + ADR, (10b) service READMEs + RunProfiles README, (10c) devs-readme + onboarding skill.

### Checkpoint: Complete
- [ ] All acceptance criteria met; full build + test green; smoke test green; profiles validate.
- [ ] Docs match code; ADR 0030 merged. Ready for review.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| stdio MCP server is launched externally with `K8S_MCP_*` env names | High | Resolve in Task 2 (pilot, early). Offer a one-release deprecation shim that still reads old names; document the new `__`/`INFRA_GATE_CONFIG_PATH` launch in `docs/mcp-clients-quirks.md`. |
| ~50 `FromEnvironment()` test call-sites | Medium | Migrate per-service inside each slice (Tasks 2–6); use in-memory `IConfiguration` (no mocks). Task 1 keeps them green until migrated. |
| Compose/deploy regression | High | Task 9 gated by `smoke-test-local`; validate `compose config` parses; regenerate committed examples. |
| `docs/configuration.md` (46 KB) drift | Medium | Dedicated Task 10; verify with `rg` for stale names; patch only real drift, preserve voice. |
| Losing the RuntimeSafety env allowlist | Medium | **Accepted**: unprefixed `AddEnvironmentVariables()` for simplicity (2026-06-02 decision); document the dropped allowlist in ADR 0030. |
| Flipping `Required` POCO default to `true` changes the unconfigured-auth path | High | Do it in Phase 2 alongside a `new DownstreamAuthOptions()` call-site audit (esp. the gateway null-provider fallback) **and** dev profiles set `required=false` (Task 7) — all in one change set. |
| Task 8/10 too large (XL) | Medium | Pre-split as noted (per-service `Program.cs` swaps; docs sub-tasks) so no task exceeds ~5 files. |
| Binder vs records | Low | Use `sealed class` settings with settable/`init` properties for bindable types; keep behavior out of them (code-standards). |

## Resolved Decisions (2026-06-02)

- **Secrets:** single gitignored `.env`; `scripts/generate-env.sh` injects secrets into that one file
  at generation time (no separate secrets overlay). `run-profiles.yaml` stays secret-free.
- **stdio:** hard-cut — no deprecation shim for `K8S_MCP_*`; document the new launch in Phase 5.
- **Allowlist:** **unprefixed** `AddEnvironmentVariables()` (no `InfraGate__` prefix filter). This
  drops the RuntimeSafety "only mapped vars are honored" posture in favour of simplicity — record the
  trade-off in ADR 0030.
- **Phase 1 pilot:** start the parser collapse on `DownstreamAuthOptions` (smallest shared type).

## Notes for implementers (repo standards)

- code-standards: `sealed` by default; file-scoped namespaces; `internal` unless cross-project;
  `ConfigureAwait(false)` in library/tool code; section-root strings as constants (no magic strings);
  `ILogger` message templates (no interpolation); validators are `sealed : IValidateOptions<T>`.
- writing-tests: tests under `tests/<Project>.Tests/UnitTests`; `Method_State_ExpectedResult`;
  `[Theory]`/`[InlineData]` over duplicated facts; **no mocks** — in-memory `IConfiguration` for
  config tests, Testcontainers for integration; add `InternalsVisibleTo` if touching internals.
- verify-readme-docs: `docs/configuration.md` is canonical; don't make aspirational claims; patch only
  real drift; verify with `git diff --check` and `rg`.
