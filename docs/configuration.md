# Configuration Reference

This is the canonical configuration reference for Kubernetes MCP Guard. Keep runnable examples in setup docs, but keep defaults, descriptions, and production guidance here.

Defaults below come from the current source code and workflows. Paths shown as `<working directory>/...` are resolved from the process working directory.

## McpServer

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `KUBECONFIG` | `InfraGate.McpServer` | No | Kubernetes client default discovery | `.kube/mcp-nginx-demo.config` | Optional kubeconfig path used by the Kubernetes client. | Use a least-privilege kubeconfig backed by namespace-scoped RBAC, not an admin kubeconfig. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpServer`, `InfraGate.McpGateway` | No | `<working directory>/.mcp-approvals` | `.mcp-approvals` | Approval plan storage root containing pending, approved, applied, challenge, and audit files. | Gateway and downstream server must share the same durable, protected storage. |
| `K8S_MCP_ALLOWED_NAMESPACES` | `InfraGate.McpServer` | No | `mcp-nginx-demo` | `mcp-nginx-demo,staging` | Comma-separated namespace allow-list. Requests outside this set are rejected before Kubernetes API calls. | Keep this aligned with Kubernetes RBAC; do not use it as a substitute for RBAC. |

## McpGateway

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` | `InfraGate.McpGateway` | No | `http://127.0.0.1:3001` when no URL config is set | `http://0.0.0.0:3001` | ASP.NET Core bind URL for the HTTP MCP gateway and browser approval endpoints. | Bind intentionally and put the gateway behind TLS in production. |
| `INFRA_GATE_DOWNSTREAM_PROJECT` | `InfraGate.McpGateway` | No | `<working directory>/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | `/repo/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | Downstream stdio MCP server project used when no published assembly is configured. | Prefer `INFRA_GATE_DOWNSTREAM_ASSEMBLY` for immutable container/runtime deployments. |
| `INFRA_GATE_DOWNSTREAM_ASSEMBLY` | `InfraGate.McpGateway` | No | Unset | `/app/server/InfraGate.McpServer.dll` | Published downstream server assembly. When set, the gateway starts `dotnet <assembly>`. | Use a known published assembly from the same release as the gateway image. |
| `INFRA_GATE_GUARD_AUDIT_ROOT` | `InfraGate.McpGateway` | No | `<working directory>/.mcp-guardrails` | `/data/guardrails` | Guardrail JSONL audit output root. | Store on protected durable storage and monitor retention. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpGateway`, `InfraGate.McpServer` | No | `<working directory>/.mcp-approvals` | `/data/approvals` | Shared approval storage used by browser approval challenges and downstream plan application. | Must be shared with the downstream server and protected from tampering. |
| `INFRA_GATE_APPROVAL_BASE_URL` | `InfraGate.McpGateway` | No | Request-derived URL, or `http://127.0.0.1:3001` when no request is available | `https://gateway.example.com` | Public base URL used when returning approval links to the MCP client. | Set explicitly to the external HTTPS URL users open in a browser. |
| `INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS` | `InfraGate.McpGateway` | No | `900` | `900` | Approval URL lifetime in seconds. | Keep short enough to limit stale approvals while allowing human review. |

## McpGateway.Auth

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `INFRA_GATE_OAUTH_AUTHORITY` | `InfraGate.McpGateway.Auth` | Yes | None | `http://127.0.0.1:3011` | OAuth/OIDC issuer URL used for JWT validation and protected-resource metadata. | Use a real HTTPS issuer in production; DevIssuer is development-only. |
| `INFRA_GATE_OAUTH_METADATA_ADDRESS` | `InfraGate.McpGateway.Auth` | No | Unset | `http://devissuer:3011/.well-known/openid-configuration` | Optional internal OIDC discovery URL when the gateway reaches the issuer through a different network address than clients use. | Use only when network topology requires it; issuer claims must still match `INFRA_GATE_OAUTH_AUTHORITY`. |
| `INFRA_GATE_OAUTH_RESOURCE` | `InfraGate.McpGateway.Auth` | No | `http://127.0.0.1:3001/mcp` | `https://gateway.example.com/mcp` | Expected JWT audience/resource and MCP protected resource value. | Set to the externally stable MCP resource URI and configure the IdP to issue it as an audience. |
| `INFRA_GATE_OAUTH_SCOPE` | `InfraGate.McpGateway.Auth` | No | `mcp:tools` | `mcp:tools` | Required scope checked on MCP requests and requested by the approval UI OAuth flow. | Keep scopes aligned with the IdP and client configuration. |
| `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA` | `InfraGate.McpGateway.Auth` | No | `true` | `false` | Controls the HTTPS requirement for OIDC discovery metadata. | `false` is acceptable only for localhost DevIssuer development. Keep `true` in production. |
| `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID` | `InfraGate.McpGateway.Auth` | No | `infra-gate-approval-ui` | `infra-gate-approval-ui` | Public OAuth client id used by the browser approval UI. | Register this as a public PKCE client with the production IdP. |
| `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/authorize` | `https://issuer.example.com/realms/demo/protocol/openid-connect/auth` | Browser-visible authorization endpoint override for approval login. | Set when the provider does not expose `/authorize` under the authority root. |
| `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/token` | `https://issuer.example.com/realms/demo/protocol/openid-connect/token` | Gateway-visible token endpoint override for approval login. | Use an endpoint reachable by the gateway; do not point browser-only hosts here. |
| `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH` | `InfraGate.McpGateway.Auth` | No | `/approvals/oauth/callback` | `/approvals/oauth/callback` | Local callback path used by the gateway approval UI OAuth flow. | Register the full external redirect URI with the IdP. |

## DevIssuer

`InfraGate.DevIssuer` is for localhost development only. It uses in-memory clients, authorization codes, and signing keys, and must not be used as a production identity provider.

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
| `SONAR_TOKEN` | GitHub Actions secret | Required for Sonar workflow | None | `sqp_...` | SonarCloud token used by `sonar.yml`. | Store as `secrets.SONAR_TOKEN`. |
| `SONAR_PROJECT_KEY` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser_Kubernetes-MCP-Guard` | SonarCloud project key passed to the scanner. | Store as `vars.SONAR_PROJECT_KEY`. |
| `SONAR_ORGANIZATION` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser` | SonarCloud organization passed to the scanner. | Store as `vars.SONAR_ORGANIZATION`. |
| `push_images` | Docker workflow dispatch input | No | `false` | `true` | Manual workflow input that requests image publishing. | Use only for intentional publishing; release tags publish automatically. |
| `PUSH_IMAGES` | Docker workflow environment | Derived | `true` on `v*` tag pushes or manual `push_images=true`; otherwise `false` | `true` | Internal `package-docker.yml` flag controlling registry login and image push. | Do not set directly outside the workflow unless testing the workflow logic. |
| `INFRA_GATE_RUN_INTEGRATION` | Test environment | No | Unset, live server integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpServer.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `INFRA_GATE_RUN_GATEWAY_INTEGRATION` | Test environment | No | Unset, live gateway integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpGateway.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `KUBECONFIG` | Tests and local scripts | No | Tests fall back to `.kube/mcp-nginx-demo.config` when unset | `.kube/mcp-nginx-demo.config` | Kubeconfig used by live integration tests and runtime examples. | Use a generated service-account kubeconfig, not the admin kubeconfig. |
| `TAG` | Release compose and smoke test | No | `latest` | `v0.1.0` | Image tag used by `deploy/mode-c/compose.release.yaml` and `scripts/smoke-test-release.sh`. | Use a fixed release tag for repeatable validation; avoid floating `latest` for release checks. |
| `KUBECONFIG_PATH` | `scripts/smoke-test-release.sh` | No | `<repo>/.kube/mcp-nginx-demo.compose.config` | `.kube/mcp-nginx-demo.compose.config` | Kubeconfig path mounted by the published-image smoke test. | Use a disposable demo kubeconfig created with `./scripts/create-demo-kubeconfig.sh --compose`. |
