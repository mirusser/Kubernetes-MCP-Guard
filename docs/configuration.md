# Configuration Reference

This is the canonical configuration reference for Kubernetes MCP Guard. Keep runnable examples in setup docs, but keep defaults, descriptions, and production guidance here.

Defaults below come from the current source code and workflows. Paths shown as `<working directory>/...` are resolved from the process working directory.

## Runtime Mode

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `INFRA_GATE_ENVIRONMENT` | All runtime components | No | `Development` when no environment variable is set; Docker images set `Production` | `Production` | InfraGate runtime mode. Valid values are `Development` and `Production`. Overrides `DOTNET_ENVIRONMENT` and `ASPNETCORE_ENVIRONMENT`. | Set `Production` for shared or real deployments. Invalid values fail startup. |
| `DOTNET_ENVIRONMENT` | All runtime components | No | Used only when `INFRA_GATE_ENVIRONMENT` is unset | `Production` | Standard .NET environment fallback. `Development` means development; all other values are treated as production-like. | Prefer `INFRA_GATE_ENVIRONMENT` for explicit InfraGate behavior. |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET components, fallback for server mode parsing | No | Used only when the two variables above are unset | `Production` | ASP.NET Core environment fallback. `Development` means development; all other values are treated as production-like. | Prefer `INFRA_GATE_ENVIRONMENT` for explicit InfraGate behavior. |

## McpServer

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `KUBECONFIG` | `InfraGate.McpServer` | Required in Production unless `K8S_MCP_USE_IN_CLUSTER=true` | Development only: Kubernetes client default discovery | `.kube/mcp-nginx-demo.config` | Optional kubeconfig path used by the Kubernetes client. | Use a least-privilege kubeconfig backed by namespace-scoped RBAC, not an admin kubeconfig. Do not rely on default discovery in Production. |
| `K8S_MCP_USE_IN_CLUSTER` | `InfraGate.McpServer` | Required in Production unless `KUBECONFIG` is set | `false` | `true` | Uses the in-cluster Kubernetes ServiceAccount instead of a kubeconfig. Cannot be combined with `KUBECONFIG`. | Set to `true` only when running inside Kubernetes with a namespace-scoped ServiceAccount and RBAC. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpServer`, `InfraGate.McpGateway` | Required in Production | `<working directory>/.mcp-approvals` | `/data/approvals` | Approval plan storage root containing pending, approved, applied, challenge, and audit files. | Gateway and downstream server must share the same durable, protected absolute path. Production refuses temp paths, default dev paths, and group/other-writable existing directories. |
| `K8S_MCP_ALLOWED_NAMESPACES` | `InfraGate.McpServer` | Required in Production | `mcp-nginx-demo` | `mcp-nginx-demo,staging` | Comma-separated namespace allow-list. Requests outside this set are rejected before Kubernetes API calls. | Keep this aligned with Kubernetes RBAC; do not use it as a substitute for RBAC. Production requires an explicit non-empty value. |

## McpGateway

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` | `InfraGate.McpGateway` | No | `http://127.0.0.1:3001` when no URL config is set | `http://0.0.0.0:3001` | ASP.NET Core bind URL for the HTTP MCP gateway and browser approval endpoints. | Bind intentionally and put the gateway behind TLS in production. |
| `INFRA_GATE_DOWNSTREAM_PROJECT` | `InfraGate.McpGateway` | No | `<working directory>/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | `/repo/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | Downstream stdio MCP server project used when no published assembly is configured. | Prefer `INFRA_GATE_DOWNSTREAM_ASSEMBLY` for immutable container/runtime deployments. |
| `INFRA_GATE_DOWNSTREAM_ASSEMBLY` | `InfraGate.McpGateway` | No | Unset | `/app/server/InfraGate.McpServer.dll` | Published downstream server assembly. When set, the gateway starts `dotnet <assembly>`. | Use a known published assembly from the same release as the gateway image. |
| `INFRA_GATE_GUARD_AUDIT_ROOT` | `InfraGate.McpGateway` | Required in Production | `<working directory>/.mcp-guardrails` | `/data/guardrails` | Guardrail JSONL audit output root. | Store on protected durable absolute storage and monitor retention. Production refuses temp paths, default dev paths, and group/other-writable existing directories. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpGateway`, `InfraGate.McpServer` | Required in Production | `<working directory>/.mcp-approvals` | `/data/approvals` | Shared approval storage used by browser approval challenges and downstream plan application. | Must be shared with the downstream server and protected from tampering. Production requires an explicit durable path. |
| `INFRA_GATE_APPROVAL_BASE_URL` | `InfraGate.McpGateway` | Required in Production | Request-derived URL, or `http://127.0.0.1:3001` when no request is available | `https://gateway.example.com` | Public base URL used when returning approval links to the MCP client. | Set explicitly to the external HTTPS URL users open in a browser. Production refuses missing, HTTP, or loopback values. |
| `INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS` | `InfraGate.McpGateway` | No | `900` | `900` | Approval URL lifetime in seconds. | Keep short enough to limit stale approvals while allowing human review. |

## McpGateway.Auth

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `INFRA_GATE_OAUTH_AUTHORITY` | `InfraGate.McpGateway.Auth` | Yes | None | `http://127.0.0.1:3011` | OAuth/OIDC issuer URL used for JWT validation and protected-resource metadata. | Use a real HTTPS issuer in production; DevIssuer is development-only. Production refuses HTTP or loopback values. |
| `INFRA_GATE_OAUTH_METADATA_ADDRESS` | `InfraGate.McpGateway.Auth` | No | Unset | `http://devissuer:3011/.well-known/openid-configuration` | Optional internal OIDC discovery URL when the gateway reaches the issuer through a different network address than clients use. | Use only when network topology requires it; issuer claims must still match `INFRA_GATE_OAUTH_AUTHORITY`. Production refuses HTTP or loopback values when set. |
| `INFRA_GATE_OAUTH_RESOURCE` | `InfraGate.McpGateway.Auth` | No | `http://127.0.0.1:3001/mcp` | `https://gateway.example.com/mcp` | Expected JWT audience/resource and MCP protected resource value. | Set to the externally stable HTTPS MCP resource URI and configure the IdP to issue it as an audience. Production refuses the localhost default. |
| `INFRA_GATE_OAUTH_SCOPE` | `InfraGate.McpGateway.Auth` | No | `mcp:tools` | `mcp:tools` | Required scope checked on MCP requests and requested by the approval UI OAuth flow. | Keep scopes aligned with the IdP and client configuration. |
| `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA` | `InfraGate.McpGateway.Auth` | No | `true` | `false` | Controls the HTTPS requirement for OIDC discovery metadata. | `false` is acceptable only for local HTTP development issuers such as DevIssuer or the Keycloak demo. Production refuses `false`. |
| `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID` | `InfraGate.McpGateway.Auth` | No | `infra-gate-approval-ui` | `infra-gate-approval-ui` | Public OAuth client id used by the browser approval UI. | Register this as a public PKCE client with the production IdP. |
| `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/authorize` | `https://issuer.example.com/realms/demo/protocol/openid-connect/auth` | Browser-visible authorization endpoint override for approval login. | Set when the provider does not expose `/authorize` under the authority root. Production requires the effective endpoint to be HTTPS and non-loopback. |
| `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/token` | `https://issuer.example.com/realms/demo/protocol/openid-connect/token` | Gateway-visible token endpoint override for approval login. | Use an endpoint reachable by the gateway; do not point browser-only hosts here. Production requires the effective endpoint to be HTTPS and non-loopback. |
| `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH` | `InfraGate.McpGateway.Auth` | No | `/approvals/oauth/callback` | `/approvals/oauth/callback` | Local callback path used by the gateway approval UI OAuth flow. | Register the full external redirect URI with the IdP. |

## DevIssuer

`InfraGate.DevIssuer` is for localhost development only. It uses in-memory clients, authorization codes, and signing keys, and refuses startup when `INFRA_GATE_ENVIRONMENT=Production`.

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` | `InfraGate.DevIssuer` | No | `http://127.0.0.1:3011` when no URL config is set | `http://0.0.0.0:3011` | ASP.NET Core bind URL for the development issuer. | Development only; replace DevIssuer with a real IdP in production. |
| `INFRA_GATE_DEV_ISSUER_ISSUER` | `InfraGate.DevIssuer` | No | `http://127.0.0.1:3011` | `http://127.0.0.1:3011` | Issuer URL written into discovery metadata and JWT `iss` claims. | Development only. |
| `INFRA_GATE_DEV_ISSUER_RESOURCE` | `InfraGate.DevIssuer` | No | `http://127.0.0.1:3001/mcp` | `http://127.0.0.1:3001/mcp` | Token audience/resource issued by DevIssuer. | Development only; align production audiences in the real IdP. |
| `INFRA_GATE_DEV_ISSUER_SCOPE` | `InfraGate.DevIssuer` | No | `mcp:tools` | `mcp:tools` | Scope issued by DevIssuer. | Development only. |
| `INFRA_GATE_DEV_ISSUER_SUBJECT` | `InfraGate.DevIssuer` | No | `infra-gate-dev-user` | `infra-gate-dev-user` | Subject claim for issued access tokens. | Development only; production identity comes from the IdP. |
| `INFRA_GATE_DEV_ISSUER_INTERNAL_ENDPOINT_BASE` | `InfraGate.DevIssuer` | No | Unset | `http://devissuer:3011` | Optional internal endpoint base for discovery metadata requested over a bridge network while preserving the public issuer value in tokens. | Development and demo compose use only. |
| `INFRA_GATE_DEV_ISSUER_APPROVAL_CLIENT_ID` | `InfraGate.DevIssuer` | No | `infra-gate-approval-ui` | `infra-gate-approval-ui` | Pre-registered approval UI client id. | Development only; configure the real IdP separately. |
| `INFRA_GATE_DEV_ISSUER_APPROVAL_REDIRECT_URI` | `InfraGate.DevIssuer` | No | `http://127.0.0.1:3001/approvals/oauth/callback` | `http://127.0.0.1:3001/approvals/oauth/callback` | Pre-registered approval UI redirect URI. | Development only. |

## CI, Release, And Scripts

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `DOCKERHUB_USERNAME` | GitHub Actions variable | Required only when pushing images | None | `mirusser` | Docker Hub username used by `package-docker.yml`. | Store as `vars.DOCKERHUB_USERNAME`; do not hard-code in workflows. |
| `DOCKERHUB_NAMESPACE` | GitHub Actions variable | Required only when pushing images | `local` for non-push build metadata | `mirusser` | Docker Hub namespace used in published image tags. | Store as `vars.DOCKERHUB_NAMESPACE`; verify repositories are public if public pulls are intended. |
| `DOCKERHUB_TOKEN` | GitHub Actions secret | Required only when pushing images | None | `dckr_pat_...` | Docker Hub token used by `package-docker.yml`. | Store as `secrets.DOCKERHUB_TOKEN` and rotate if exposed. |
| `GITHUB_TOKEN` | GitHub Actions secret | Required for GHCR publishing | GitHub-provided token | `${{ secrets.GITHUB_TOKEN }}` | Token used by GitHub Actions to publish GHCR images. | Keep workflow permissions narrow; current package workflow grants `packages: write`. |
| `DEPLOY_PATH` | GitHub Actions environment variable | No | `/opt/infra-gate` for development | `/opt/infra-gate` | Directory where the development workflow copies `compose.yaml` before running Docker Compose on the self-hosted runner. | Use a path owned by the runner user and prepared by `scripts/setup-development-deploy.sh`. |
| `RUNNER_USER` | `scripts/setup-development-deploy.sh` | No | `SUDO_USER` | `actions-runner` | Local user that runs the development self-hosted GitHub Actions runner. The setup script uses it to make the deploy path and env file readable/writable by the runner and to generate the demo kubeconfig with the user's Kubernetes context. | Set explicitly when running the setup script from a different sudo user than the runner account. |
| `KEYCLOAK_PORT` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `3010` | `3010` | Host port for the local Keycloak development issuer. | Keep the default unless the Keycloak realm audience/redirects are updated and re-imported. |
| `KEYCLOAK_BIND_ADDRESS` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `0.0.0.0` from setup script; `127.0.0.1` in direct Compose usage | `0.0.0.0` | Host bind address for the local Keycloak development issuer. The setup script binds broadly so the gateway container can reach Keycloak through the Docker bridge while browser URLs stay on loopback. | Use only for local development; do not expose this demo issuer on shared networks. |
| `GATEWAY_PORT` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `3001` | `3001` | Host port used for the local gateway URLs and Keycloak audience/redirect alignment. | The bundled Keycloak realm expects `http://127.0.0.1:3001/mcp`; update and re-import the realm before changing it. |
| `SONAR_TOKEN` | GitHub Actions secret | Required for Sonar workflow | None | `sqp_...` | SonarCloud token used by `sonar.yml`. | Store as `secrets.SONAR_TOKEN`. |
| `SONAR_PROJECT_KEY` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser_Kubernetes-MCP-Guard` | SonarCloud project key passed to the scanner. | Store as `vars.SONAR_PROJECT_KEY`. |
| `SONAR_ORGANIZATION` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser` | SonarCloud organization passed to the scanner. | Store as `vars.SONAR_ORGANIZATION`. |
| `push_images` | Docker workflow dispatch input | No | `false` | `true` | Manual workflow input that requests image publishing. | Use only for intentional publishing; release tags publish automatically. |
| `PUSH_IMAGES` | Docker workflow environment | Derived | `true` on `dev`, `v*` tag pushes, or manual `push_images=true`; otherwise `false` | `true` | Internal `package-docker.yml` flag controlling registry login and image push. | Do not set directly outside the workflow unless testing the workflow logic. |
| `INFRA_GATE_RUN_INTEGRATION` | Test environment | No | Unset, live server integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpServer.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `INFRA_GATE_RUN_GATEWAY_INTEGRATION` | Test environment | No | Unset, live gateway integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpGateway.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `Category=Keycloak` (xUnit trait) | Test environment | No | Keycloak tests excluded from default runs | `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` | Enables real-OIDC integration tests against a Testcontainers Keycloak container. | Requires Docker. The `infra-gate-realm.json` test data file is loaded as the realm config. Separate CI workflow at `keycloak-tests.yml`. |
| `KUBECONFIG` | Tests and local scripts | No | Tests fall back to `.kube/mcp-nginx-demo.config` when unset | `.kube/mcp-nginx-demo.config` | Kubeconfig used by live integration tests and runtime examples. | Use a generated service-account kubeconfig, not the admin kubeconfig. |
| `TAG` | Docker Compose and smoke test | No for development/demo; required by production Compose | `dev` in development deploy, `latest` in demo release Compose | `v0.1.0` | Image tag used by `deploy/compose/*.yaml`, `deploy/mode-c/compose.release.yaml`, and `scripts/smoke-test-release.sh`. | Use the raw release tag when running production Compose manually; avoid floating tags for production. |
| `INFRA_GATE_GATEWAY_IMAGE` | Docker Compose deploys | No | `ghcr.io/mirusser/kubernetes-mcp-guard-gateway` | `mirusser/kubernetes-mcp-guard-gateway` | Gateway image repository used by deployment Compose files. | Override only when deploying from Docker Hub or a private mirror. |
| `INFRA_GATE_KUBECONFIG_HOST_PATH` | Docker Compose deploys | No | `/etc/infra-gate/<environment>.kubeconfig` | `/etc/infra-gate/production.kubeconfig` | Host kubeconfig path mounted read-only into the gateway container as `/run/kube/infra-gate.config`. | Use a least-privilege kubeconfig; keep permissions tight on the host. |
| `INFRA_GATE_APPROVAL_HOST_PATH` | Docker Compose deploys | No | `/var/lib/infra-gate/<environment>/approvals` | `/var/lib/infra-gate/production/approvals` | Host path mounted into the container as `/data/approvals`. | Use durable storage that is not group- or other-writable. |
| `INFRA_GATE_GUARD_AUDIT_HOST_PATH` | Docker Compose deploys | No | `/var/lib/infra-gate/<environment>/guardrails` | `/var/lib/infra-gate/production/guardrails` | Host path mounted into the container as `/data/guardrails`. | Use durable storage with retention/monitoring appropriate for audit logs. |
| `INFRA_GATE_BIND_ADDRESS` | Docker Compose deploys | No | `127.0.0.1` | `127.0.0.1` | Host bind address for the gateway container port. | Keep loopback when TLS terminates at a host reverse proxy. |
| `INFRA_GATE_BIND_PORT` | Docker Compose deploys | No | `3001` | `3001` | Host port mapped to the gateway container. The deploy workflow probes this port after `docker compose up`. | Match the local reverse proxy upstream. |
| `KUBECONFIG_PATH` | `scripts/smoke-test-release.sh` | No | `<repo>/.kube/mcp-nginx-demo.compose.config` | `.kube/mcp-nginx-demo.compose.config` | Kubeconfig path mounted by the published-image smoke test. | Use a disposable demo kubeconfig created with `./scripts/create-demo-kubeconfig.sh --compose`. |
