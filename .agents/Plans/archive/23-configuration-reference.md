# Epic 7: Configuration Reference

## Summary
Create `docs/configuration.md` as the canonical configuration reference for runtime, local demo, and CI/CD settings. No runtime behavior, environment variable names, defaults, or public code APIs change.

Current source of truth will be the implementation and workflow files, especially option/convention classes under `src/`, compose files under `deploy/mode-c/`, workflows under `.github/workflows/`, and release scripts under `scripts/`.

## Key Changes
- Add `docs/configuration.md` with one table per area using columns: Variable, Component, Required, Default, Example, Description, Production guidance.
- Document these runtime variables:
  - McpServer: `KUBECONFIG`, `K8S_MCP_APPROVAL_ROOT`, `K8S_MCP_ALLOWED_NAMESPACES`.
  - McpGateway: `ASPNETCORE_URLS`, `INFRA_GATE_DOWNSTREAM_PROJECT`, `INFRA_GATE_DOWNSTREAM_ASSEMBLY`, `INFRA_GATE_GUARD_AUDIT_ROOT`, `K8S_MCP_APPROVAL_ROOT`, `INFRA_GATE_APPROVAL_BASE_URL`, `INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS`.
  - McpGateway.Auth: `INFRA_GATE_OAUTH_AUTHORITY`, `INFRA_GATE_OAUTH_METADATA_ADDRESS`, `INFRA_GATE_OAUTH_RESOURCE`, `INFRA_GATE_OAUTH_SCOPE`, `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA`, `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID`, `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT`, `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT`, `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH`.
  - DevIssuer: `ASPNETCORE_URLS`, `INFRA_GATE_DEV_ISSUER_ISSUER`, `INFRA_GATE_DEV_ISSUER_RESOURCE`, `INFRA_GATE_DEV_ISSUER_SCOPE`, `INFRA_GATE_DEV_ISSUER_SUBJECT`, `INFRA_GATE_DEV_ISSUER_INTERNAL_ENDPOINT_BASE`, `INFRA_GATE_DEV_ISSUER_APPROVAL_CLIENT_ID`, `INFRA_GATE_DEV_ISSUER_APPROVAL_REDIRECT_URI`.
- Add a CI/CD and release section for repo variables, secrets, workflow inputs, and script overrides: `DOCKERHUB_USERNAME`, `DOCKERHUB_NAMESPACE`, `DOCKERHUB_TOKEN`, `GITHUB_TOKEN`, `SONAR_TOKEN`, `SONAR_PROJECT_KEY`, `SONAR_ORGANIZATION`, `push_images`, `PUSH_IMAGES`, `INFRA_GATE_RUN_INTEGRATION`, `INFRA_GATE_RUN_GATEWAY_INTEGRATION`, `KUBECONFIG`, `TAG`, and `KUBECONFIG_PATH`.
- Flag dangerous or dev-only settings clearly:
  - `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false` is localhost DevIssuer only.
  - DevIssuer is development-only.
  - Production `KUBECONFIG` should represent least-privilege, namespace-scoped access, not admin access.
  - `K8S_MCP_APPROVAL_ROOT` must be shared by gateway and downstream server and should live on protected durable storage in non-demo deployments.
- Update `README.md` to link to `docs/configuration.md` from the project map.
- Replace duplicated environment-variable reference sections in `docs/setup-guide.md`, `docs/devs-readme.md`, and the four runtime project READMEs with links to `docs/configuration.md`.
- Keep runnable shell snippets and compose examples where they are needed to run the project; remove or shorten prose/table descriptions that duplicate the new reference.
- Leave contextual mentions in security, OIDC, MCP compliance, demo, and troubleshooting docs when the variable itself is part of the explanation.

## Test Plan
- Run `git diff --check`.
- Verify the new doc is linked from README and relevant runbooks with `rg -n "configuration.md" README.md docs src/*/README.md`.
- Verify detailed duplicate reference tables/lists were removed from non-canonical docs with `rg -n "Environment Variable Reference|## Configuration" docs src/*/README.md`.
- Review remaining variable mentions with `rg -n "INFRA_GATE|K8S_MCP|KUBECONFIG|ASPNETCORE_URLS" README.md docs src/*/README.md` and confirm they are runnable examples or contextual explanations, not competing references.
- No `dotnet test` is required because this is docs-only and does not change code paths.

## Assumptions
- “Single source of truth” means descriptions, defaults, examples, and production guidance live in `docs/configuration.md`; executable setup snippets may still show env vars inline.
- Defaults come from current code, not older docs. In particular, Gateway `ASPNETCORE_URLS` defaults to `http://127.0.0.1:3001`, and DevIssuer defaults to `http://127.0.0.1:3011` when no URL config is provided.
- CI/CD coverage should document user-controlled repo settings and meaningful workflow/script toggles, not every temporary shell variable inside a workflow step.
