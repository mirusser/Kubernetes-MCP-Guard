# InfraGate.RunProfiles

CLI tool that compiles named profiles from `deploy/run-profiles.yaml` into `.env` files for Docker Compose/deployment scripts and appsettings JSON for .NET runtime binding. It is the canonical source of truth for all runnable environment configuration.

## Commands

```bash
# List available profiles
dotnet run --project src/InfraGate.RunProfiles -- list

# Validate all profiles parse without error
dotnet run --project src/InfraGate.RunProfiles -- validate

# Generate an env file or appsettings JSON from a profile
dotnet run --project src/InfraGate.RunProfiles -- generate <profile> [options]
```

### `generate` options

| Flag | Description |
|---|---|
| `--format env\|appsettings` | Output format (default: `env`) |
| `--output <path>` | Write to a file instead of stdout |
| `--set section.field=value` | Override a single field after profile merge; repeatable |
| `--force` | Overwrite an existing output file (default: refuse) |
| `--config <path>` | Use an alternate YAML file (default: `deploy/run-profiles.yaml`) |

## Profile catalogue

| Profile | Kind | Purpose |
|---|---|---|
| `local-compose` | `compose` | Local Compose stack built from source via `deploy/local-oauth/compose.yaml` |
| `local-source-gateway` | `dotnet` | Gateway run from source (`dotnet run`) against local Keycloak |
| `local-stdio` | `mcp-stdio` | MCP server run from source as a stdio subprocess |
| `development` | `compose-deploy` | Self-hosted development deployment via `deploy/compose/development.yaml` |
| `production` | `compose-deploy` | Production deployment via `deploy/compose/production.yaml` |
| `test-integration` | `test` | MCP server live integration tests |
| `test-gateway-integration` | `test` | Gateway live integration tests |
| `test-safety-e2e` | `test` | Safety E2E tests in KinD |
| `smoke-local` | `smoke` | Smoke test against local-build Compose stack |
| `smoke-release` | `smoke` | Smoke test against published image Compose stack |

## YAML schema

```yaml
version: 1

defaults:
  gateway:
    aspnetcoreUrls: http://0.0.0.0:3001
    downstreamAssembly: /app/server/InfraGate.McpServer.dll
    guardAuditRoot: /data/guardrails
  identityProvider:
    scope: mcp:tools
    requireHttpsMetadata: "false"
  approvalAuthority:
    oauthClientId: infra-gate-approval-ui
    oauthCallbackPath: /approvals/oauth/callback

profiles:
  <name>:
    kind: compose | dotnet | mcp-stdio | compose-deploy | test | smoke
    runtimeMode: Development | Production

    # Section opt-in: only sections declared in the profile merge with defaults.
    # Use `gateway: {}` to inherit all gateway defaults without adding any fields.
    gateway:
      aspnetcoreUrls: <url>
      downstreamAssembly: <path>
      guardAuditRoot: <path>

    identityProvider:
      authority: <url>
      metadataAddress: <url>       # optional; internal discovery endpoint
      resource: <url>
      scope: <scope>
      requireHttpsMetadata: "true" | "false"
      realmImport: <path>          # informational; Keycloak realm JSON path
      oauthClientId: <id>
      oauthCallbackPath: <path>

    approvalAuthority:
      baseUrl: <url>
      oauthClientId: <id>
      oauthCallbackPath: <path>
      oauthAuthorizationEndpoint: <url>
      oauthTokenEndpoint: <url>

    genericApprovalCore:
      approvalRoot: <path>
      postgresConnectionString: <connection_string>
      runMigrationsOnStartup: <"true" or "false">

    host:                          # Compose bind-mount and image configuration
      bindAddress: <address>
      bindPort: "<port>"
      gatewayImage: <image>
      kubeconfigHostPath: <path>
      approvalHostPath: <path>
      guardAuditHostPath: <path>
      dataProtectionHostPath: <path>

    domainAdapters:
      - name: kubernetesAdapter
        type: kubernetes
        kubernetes:
          kubeconfig: <path>
          allowedNamespaces:
            - <namespace>

    observer:
      aspnetcoreUrls: <url>
      gatewayBaseUrl: <url>
      oauthAuthority: <url>
      clientId: <id>
      clientSecret: <secret>
      scope: <scope>
      llmProvider: <provider>
      llmModel: <model>
      llmApiKey: <secret>
      cycleCadenceSeconds: "<seconds>"
      cycleWallClockCapSeconds: "<seconds>"
      maxToolIterations: "<count>"
      fileSinkRoot: <path>
      plannerHandoffUrl: <url>
      observerHostPath: <host-path>

    planner:
      aspnetcoreUrls: <url>
      gatewayBaseUrl: <url>
      executorHandoffUrl: <url>
      tokenEndpoint: <url>
      clientId: <id>
      clientSecret: <secret>
      oauthAuthority: <url>
      scope: <scope>
      llmProvider: <provider>
      llmModel: <model>
      llmApiKey: <secret>
      anomalyWallClockCapSeconds: "<seconds>"
      batchWallClockCapSeconds: "<seconds>"
      maxToolIterations: "<count>"
      fileSinkRoot: <path>
      plannerHostPath: <host-path>

    executor:
      aspnetcoreUrls: <url>
      gatewayBaseUrl: <url>
      tokenEndpoint: <url>
      clientId: <id>
      clientSecret: <secret>
      oauthAuthority: <url>
      scope: <scope>
      concurrencyCap: "<count>"
      watchTimeoutSeconds: "<seconds>"
      executorHostPath: <host-path>
```

## Section opt-in inheritance

A profile inherits `defaults` values only for sections it explicitly declares. A profile that omits `gateway:` entirely produces no gateway env vars — this keeps test profiles (`kind: test`) free of Compose-only configuration such as `ASPNETCORE_URLS`.

To opt in to all gateway defaults without adding any profile-specific fields, write:

```yaml
profiles:
  my-profile:
    gateway: {}
```

## `--set` overrides

`--set` is applied after the profile is merged with defaults. The path format is `section.field` where section is the camelCase YAML key:

| Section | Example fields |
|---|---|
| `gateway` | `aspnetcoreUrls`, `downstreamAssembly`, `guardAuditRoot` |
| `identityProvider` | `authority`, `metadataAddress`, `resource`, `scope`, `requireHttpsMetadata` |
| `approvalAuthority` | `baseUrl`, `oauthAuthorizationEndpoint`, `oauthTokenEndpoint` |
| `genericApprovalCore` | `approvalRoot` |
| `genericApprovalCore` | `postgresConnectionString` |
| `genericApprovalCore` | `runMigrationsOnStartup` |
| `host` | `bindAddress`, `bindPort`, `gatewayImage`, `configHostPath`, `kubeconfigHostPath`, `approvalHostPath`, `guardAuditHostPath`, `dataProtectionHostPath` |
| `observer` | `gatewayBaseUrl`, `oauthAuthority`, `clientId`, `clientSecret`, `scope`, `llmModel`, `fileSinkRoot`, `plannerHandoffUrl`, `observerHostPath` |
| `planner` | `gatewayBaseUrl`, `executorHandoffUrl`, `clientId`, `clientSecret`, `oauthAuthority`, `scope`, `llmModel`, `fileSinkRoot`, `plannerHostPath` |
| `executor` | `gatewayBaseUrl`, `clientId`, `clientSecret`, `oauthAuthority`, `scope`, `concurrencyCap`, `watchTimeoutSeconds`, `executorHostPath` |

Use `--set` when a run needs host paths different from the profile defaults. Docker Compose resolves relative bind-mount paths from the Compose file directory, so local OAuth profiles keep committed defaults relative to `deploy/local-oauth/`. For generated local runs, `scripts/generate-env.sh` supplies absolute repository-root paths so the command is independent of the current working directory.

`scripts/generate-env.sh` handles this automatically for local runs:

```bash
./scripts/generate-env.sh local-compose
# → writes deploy/generated/local-compose.env and deploy/generated/local-compose.appsettings.json
#   with absolute REPO_ROOT-based host paths
```

To call the generator directly:

```bash
dotnet run --project src/InfraGate.RunProfiles -- generate local-compose \
  --output deploy/generated/local-compose.env

dotnet run --project src/InfraGate.RunProfiles -- generate local-compose \
  --format appsettings \
  --output deploy/generated/local-compose.appsettings.json
```

## Generated file layout

The generated `.env` file groups vars into labelled sections and includes a header indicating the source profile:

```
# Generated from run-profiles.yaml profile: <name>
# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate <name>

# Runtime
INFRA_GATE_ENVIRONMENT=...

# Gateway
ASPNETCORE_URLS=...
INFRA_GATE_DOWNSTREAM_ASSEMBLY=...
INFRA_GATE_GUARD_AUDIT_ROOT=...

# Identity Provider
INFRA_GATE_OAUTH_AUTHORITY=...
...

# Approval Authority
INFRA_GATE_APPROVAL_BASE_URL=...
...

# Generic Approval Core
K8S_MCP_APPROVAL_ROOT=...

# The generated appsettings JSON also includes an InfraGate:Approval:Postgres:ConnectionString
# section when the profile specifies postgresConnectionString. When runMigrationsOnStartup
# is "true", the gateway applies pending migrations at startup (development profiles only).

# Kubernetes Adapter
KUBECONFIG=...
K8S_MCP_ALLOWED_NAMESPACES=...

# Host
INFRA_GATE_BIND_ADDRESS=...
INFRA_GATE_CONFIG_PATH=/app/config/appsettings.InfraGate.json
INFRA_GATE_CONFIG_HOST_PATH=...
...

# Observer / Planner / Executor
# Generated when the profile declares the corresponding sections.
```

The generated appsettings file carries the .NET runtime values under `InfraGate:*` sections. The gateway loads the file named by `INFRA_GATE_CONFIG_PATH`, and the downstream MCP server inherits that same bootstrap env var from the gateway process.

Sections are omitted when the profile produces no values for them.

## Output paths and gitignore

Generated files belong in `deploy/generated/`, which is covered by `.gitignore`. The committed no-SDK release examples are `deploy/local-oauth/release.env.example` and `deploy/local-oauth/release.appsettings.json`, both generated from the `smoke-release` profile:

```bash
dotnet run --project src/InfraGate.RunProfiles -- generate smoke-release \
  --output deploy/local-oauth/release.env.example

dotnet run --project src/InfraGate.RunProfiles -- generate smoke-release \
  --format appsettings \
  --output deploy/local-oauth/release.appsettings.json
```

Regenerate and commit both files whenever the `smoke-release` profile or its merged defaults change.

## Secret handling

`deploy/run-profiles.yaml` must not contain secrets. It is checked into version control and is intended to hold non-secret configuration values only. Dynamic secrets (tokens, passwords) are injected at runtime through environment-specific mechanisms outside the profile system.

## CI integration

All CI workflows validate profiles before running tests:

```bash
dotnet run --project src/InfraGate.RunProfiles -- validate
```

This catches YAML parse errors and unknown field references before any test or deployment step runs.
