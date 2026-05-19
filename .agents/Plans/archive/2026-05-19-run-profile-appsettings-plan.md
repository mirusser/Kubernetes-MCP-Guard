# Implementation Plan: Run Profile Generated Appsettings

## Overview

Keep Run Profiles as the source of truth, but generate two artifacts from each profile: the existing `.env` file for Docker Compose and legacy env transport, plus a generated appsettings JSON file for .NET configuration binding. Docker Compose should consume the generated `.env` wholesale instead of enumerating runtime settings, and the gateway/server projects should bind options through `IConfiguration`.

## Architecture Decisions

- Run Profiles remain canonical; generated `.env` and generated appsettings JSON are adapters, not hand-maintained config sources.
- Add `FromConfiguration(IConfiguration)` to runtime option modules; keep `FromEnvironment()` as compatibility wrappers.
- Configuration precedence: environment variables and `InfraGate__...` overrides win over generated JSON, then existing defaults apply.
- Downstream pass-through is via `INFRA_GATE_CONFIG_PATH`: the gateway loads the mounted JSON and inherits that same env var into the MCP server subprocess.
- Keep changes surgical: do not introduce a broad config framework; add constants for new env keys, JSON sections, and CLI format names.

## Public Interfaces

- Extend RunProfiles CLI:
  - `generate <profile> --format env|appsettings --output <path>`
  - Default remains `--format env`.
- Add bootstrap env vars:
  - `INFRA_GATE_CONFIG_PATH`: container/runtime path to generated appsettings JSON.
  - `INFRA_GATE_CONFIG_HOST_PATH`: host path mounted by Docker Compose.
- Generated JSON shape:

```json
{
  "InfraGate": {
    "Runtime": {},
    "Gateway": {},
    "Auth": {},
    "Approval": {},
    "Kubernetes": {}
  }
}
```

- Map current Run Profile values into those sections: runtime mode, gateway URLs/downstream/audit paths, OAuth settings, approval root/base URL, kubeconfig, and allowed namespaces.

## Task List

### Phase 1: Generation Contract

**Task 1: Add appsettings renderer**
Description: Add a deterministic `AppSettingsRenderer` that converts a resolved Run Profile into the `InfraGate` JSON schema.

Acceptance criteria:
- Generated JSON contains only runtime values consumed by .NET projects.
- Host-only Compose values stay in `.env`.
- Output is deterministic and covered by tests.

Verification:
- `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`

Dependencies: None
Likely touched: `src/InfraGate.RunProfiles`, RunProfiles unit tests
Scope: Medium

**Task 2: Extend RunProfiles CLI and generation script**
Description: Add `--format env|appsettings`, keep env default behavior, and update `scripts/generate-env.sh` so one command emits both `deploy/generated/<profile>.env` and `deploy/generated/<profile>.appsettings.json`.

Acceptance criteria:
- Existing env generation tests still pass unchanged.
- Unknown formats and overwrite protection are tested.
- Script help explains both outputs.

Verification:
- `dotnet run --project src/InfraGate.RunProfiles -- validate`
- `./scripts/generate-env.sh local-compose --force`

Dependencies: Task 1
Likely touched: `RunProfileCli.cs`, `scripts/generate-env.sh`, RunProfiles tests
Scope: Medium

### Checkpoint: Generation

- RunProfiles validation passes.
- Generated env and JSON files are both produced from the same profile.
- No hand-authored runtime values are required for local Compose.

### Phase 2: Runtime Binding

**Task 3: Bind gateway/server options from IConfiguration**
Description: Add `FromConfiguration(IConfiguration)` to gateway auth/options, server options, and runtime mode resolution. Update `Program.cs` in gateway and server to load `INFRA_GATE_CONFIG_PATH` before binding options, then allow env vars to override JSON.

Acceptance criteria:
- Existing env-based behavior remains compatible.
- Generated JSON alone can configure gateway and server.
- Explicit production-safety checks still distinguish configured values from defaults.

Verification:
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- `dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj`

Dependencies: Task 1
Likely touched: gateway/server option types, runtime safety resolver, related tests
Scope: Medium

**Task 4: Wire Docker Compose to generated artifacts**
Description: Mount `${INFRA_GATE_CONFIG_HOST_PATH}` into the gateway container and set/pass `INFRA_GATE_CONFIG_PATH` through the generated `.env`. Keep Compose using `env_file` for the full generated profile instead of listing OAuth/Kubernetes/approval settings inline.

Acceptance criteria:
- Local and release compose files consume generated/env example files consistently.
- Gateway container can load appsettings JSON.
- MCP server subprocess receives `INFRA_GATE_CONFIG_PATH` through inherited env.

Verification:
- `docker compose --env-file deploy/generated/local-compose.env -f deploy/local-oauth/compose.yaml config`
- `docker compose --env-file deploy/local-oauth/release.env.example -f deploy/local-oauth/compose.release.yaml config`

Dependencies: Tasks 1-3
Likely touched: `deploy/local-oauth` compose files, release env/example artifacts, smoke scripts if path overrides are needed
Scope: Medium

### Checkpoint: Runtime Flow

- Compose config renders without missing variables.
- Gateway and downstream server have the same generated config path.
- Legacy flat env overrides still work.

### Phase 3: Tests and Docs

**Task 5: Add focused tests**
Description: Add unit coverage in the matching test projects using the repo naming convention `Method_State_ExpectedResult`.

Acceptance criteria:
- RunProfiles tests cover JSON rendering, CLI format selection, and release artifact consistency.
- Gateway/Auth/Server option tests cover JSON binding and env override precedence.
- Runtime mode tests cover `INFRA_GATE_ENVIRONMENT`, JSON value, `DOTNET_ENVIRONMENT`, and default ordering.

Verification:
- Same project-specific `dotnet test` commands from Tasks 1 and 3.

Dependencies: Tasks 1-3
Likely touched: `tests/InfraGate.RunProfiles.Tests`, gateway/server/runtime safety tests
Scope: Medium

**Task 6: Simplify README quick start**
Description: Update root README quick start so the common local flow is easy: create kubeconfig, generate profile artifacts, run Compose. Keep release quick start using committed example artifacts generated from `smoke-release`.

Acceptance criteria:
- Quick start avoids manual per-setting env exports.
- README clearly says generated files come from Run Profiles.
- Docs mention env vars are still valid overrides, not the preferred authoring surface.

Verification:
- `git diff --check`
- Compose config commands from Task 4

Dependencies: Tasks 2 and 4
Likely touched: `README.md`, possibly `docs/devs-readme.md`
Scope: Small

## Assumptions

- The requested `.codex/skills/...` paths map to the repo-local `.agents/skills/...` files available in this workspace.
- `K8S_MCP_LOG_PATH` may remain as a local operational override unless the implementation finds it can be moved into the new JSON binding without expanding the Run Profile schema awkwardly.
- No secrets are introduced into generated appsettings JSON; current profiles already contain development/demo values only.
