# Implementation Plan: F-07 JWT Bearer Replay Mitigation

## Overview

Mitigate F-07 from `.agents/Plans/loose/security-audit.md` by sender-constraining inbound Gateway OAuth access tokens so a stolen JWT cannot be replayed against `/mcp` by a caller that does not hold the matching private key. The primary mitigation is DPoP for the HTTP Gateway OAuth boundary, with short token lifetime and token-storage cleanup kept as defense in depth. Same-subject plan binding from F-08 is out of scope for this plan because it is a separate mitigation path.

## Assumptions

- The first target is inbound client-to-Gateway OAuth traffic, including human MCP clients and controlled service clients such as Observer, Planner, and Executor.
- Downstream Gateway-to-McpServer stdio service-token replay remains governed by `docs/adr/0008-use-stdio-service-tokens-for-downstream-mcp-auth.md` and should not be mixed into this change unless a separate downstream transport hardening task is opened.
- Local Keycloak already uses `quay.io/keycloak/keycloak:26.6.1`, which is new enough for supported DPoP configuration.
- The realm currently has `accessTokenLifespan` set to 300 seconds, so token lifetime reduction is not the main missing control.
- Production readiness depends on whether Gateway runs as a single replica or multiple replicas. Multi-replica DPoP proof replay prevention needs a shared replay store before F-07 can be called fully mitigated.

## Success Criteria

- DPoP-bound tokens are rejected unless each Gateway request includes a valid DPoP proof.
- The proof key thumbprint matches the access token `cnf.jkt` claim.
- The proof is bound to the actual HTTP method and URI.
- The proof `ath` claim matches the presented access token.
- Reusing the same proof `jti` inside the accepted proof lifetime is rejected.
- Raw access tokens and DPoP proofs are not stored in approval cookies or emitted to logs.
- Keycloak integration coverage proves the local realm can issue and require DPoP-bound tokens for the chosen clients.

## Architecture Decisions

- Use DPoP, not mTLS, for the first mitigation because the Gateway is an HTTP resource server consumed by CLI/MCP clients where application-layer sender constraints fit better than transport-layer client certificates.
- Keep DPoP validation inside `InfraGate.McpGateway.Auth`, near existing JWT validation in `GatewayAuthentication`, instead of pushing it down into tool dispatch or approval services.
- Do not rework plan ownership or same-subject authorization in this plan. F-08 is separate from F-07.
- Add a replay-store abstraction early so tests can enforce proof replay behavior while the implementation can start with the smallest deployment-appropriate store.
- Keep compatibility explicit. If a client cannot produce DPoP proofs, it should remain in a documented compatibility path with limited scopes and short-lived bearer tokens until it can be upgraded.

## Dependency Graph

```text
DPoP validation contract and test vectors
    |
    +-- DPoP proof validator
    |       |
    |       +-- replay store
    |       |
    |       +-- JwtBearerEvents integration
    |
    +-- client proof generation support
            |
            +-- Keycloak realm DPoP enforcement
            |
            +-- controlled service-client rollout
            |
            +-- external MCP client compatibility decision
```

## Task List

### Phase 1: Foundation

- [ ] Task 1: Add F-07 authentication regression tests
- [ ] Task 2: Add DPoP validation contract and replay-store contract
- [ ] Task 3: Wire DPoP validation into Gateway bearer authentication

## Task 1: Add F-07 authentication regression tests

**Description:** Add focused tests that describe the replay gap and the expected DPoP-protected behavior before implementation. These tests should live next to the existing Gateway JWT tests so the auth behavior remains visible in one place.

**Acceptance criteria:**

- [ ] A DPoP-bound access token without a `DPoP` proof header is rejected.
- [ ] DPoP proofs with wrong `ath`, wrong `htm`, wrong `htu`, wrong key thumbprint, expired `iat`, or reused `jti` are rejected.
- [ ] A valid DPoP-bound token and fresh matching proof is accepted.

**Verification:**

- [ ] Tests fail before implementation: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayAuthenticationTests`
- [ ] No production code is changed in this task.

**Dependencies:** None

**Files likely touched:**

- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DpopProofTestFactory.cs`

**Estimated scope:** Medium: 2 files

## Task 2: Add DPoP validation contract and replay-store contract

**Description:** Add the internal auth-layer types that validate a DPoP proof independently of ASP.NET wiring. Keep the surface small: one validator, one result type if needed, and one replay-store abstraction with a test-friendly implementation.

**Acceptance criteria:**

- [ ] The validator checks proof JWT signature, `typ`, asymmetric `alg`, embedded public JWK, `jti`, `htm`, `htu`, `iat`, `ath`, and access-token `cnf.jkt`.
- [ ] The replay-store contract rejects repeated `(issuer, client or subject, jti)` values within the configured proof lifetime.
- [ ] The validator never logs or returns raw access tokens or raw proof JWTs.

**Verification:**

- [ ] Tests pass: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter Dpop`
- [ ] Build succeeds: `dotnet build src/InfraGate.McpGateway.Auth/InfraGate.McpGateway.Auth.csproj`

**Dependencies:** Task 1

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/Dpop/DpopProofValidator.cs`
- `src/InfraGate.McpGateway.Auth/Dpop/IDpopProofReplayStore.cs`
- `src/InfraGate.McpGateway.Auth/Dpop/InMemoryDpopProofReplayStore.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DpopProofValidatorTests.cs`

**Estimated scope:** Medium: 4 files

## Task 3: Wire DPoP validation into Gateway bearer authentication

**Description:** Integrate the DPoP validator into the existing Gateway auth pipeline around `JwtBearerEvents`, after normal JWT issuer, audience, lifetime, and signature validation succeed. Keep the existing scope enforcement in `ToolScopeGuard` unchanged.

**Acceptance criteria:**

- [ ] When DPoP is required, `Authorization: Bearer <token>` is rejected for Gateway resource access.
- [ ] `Authorization: DPoP <token>` plus a valid `DPoP` header authenticates the request.
- [ ] Authentication failures return 401 without exposing token or proof contents.

**Verification:**

- [ ] Tests pass: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayAuthenticationTests`
- [ ] Build succeeds: `dotnet build src/InfraGate.McpGateway.Auth/InfraGate.McpGateway.Auth.csproj`

**Dependencies:** Task 2

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs`
- `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs`
- `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`

**Estimated scope:** Medium: 4 files

## Checkpoint: Foundation

- [ ] Gateway auth unit tests pass.
- [ ] `InfraGate.McpGateway.Auth` builds cleanly with warnings as errors.
- [ ] A human reviews the DPoP validation contract before client rollout begins.

### Phase 2: Core Client and Identity Provider Rollout

- [ ] Task 4: Add DPoP support to controlled client-credentials callers
- [ ] Task 5: Configure Keycloak DPoP enforcement for controlled clients
- [ ] Task 6: Decide and implement external MCP client compatibility behavior

## Task 4: Add DPoP support to controlled client-credentials callers

**Description:** Extend the shared client-credentials auth path so InfraGate-owned services can request DPoP-bound tokens and attach a fresh proof to every Gateway request.

**Acceptance criteria:**

- [ ] `InfraGate.ClientCredentials` can generate or load a DPoP key pair for a service client.
- [ ] Token requests can include a DPoP proof so the issuer returns a `cnf.jkt`-bound access token.
- [ ] Gateway HTTP requests include `Authorization: DPoP <token>` and a request-specific `DPoP` proof.

**Verification:**

- [ ] Tests pass: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter ClientCredentials`
- [ ] Tests pass for any direct client-credentials test project touched by the change.

**Dependencies:** Task 3

**Files likely touched:**

- `src/InfraGate.ClientCredentials/ClientCredentialsTokenOptions.cs`
- `src/InfraGate.ClientCredentials/ClientCredentialsTokenProvider.cs`
- `src/InfraGate.ClientCredentials/ClientCredentialsBearerHandler.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamAuth/ClientCredentialsDownstreamServiceTokenProviderTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamAuth/ClientCredentialsTokenProviderRedactionTests.cs`

**Estimated scope:** Medium: 5 files

## Task 5: Configure Keycloak DPoP enforcement for controlled clients

**Description:** Update the local/test Keycloak realm so controlled InfraGate clients issue DPoP-bound access tokens. Use a Keycloak 26.6.1 export or Admin API output as the source of truth rather than guessing JSON attribute names.

**Acceptance criteria:**

- [ ] `infra-gate-observer`, `infra-gate-planner`, and `infra-gate-executor` require DPoP-bound access tokens.
- [ ] `deploy/keycloak/infra-gate-realm.json` and `tests/TestData/keycloak/infra-gate-realm.json` remain equivalent.
- [ ] Keycloak integration tests prove a controlled client can acquire a DPoP-bound token and call the Gateway.

**Verification:**

- [ ] Realm alignment passes: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter KeycloakRealmFileTests`
- [ ] Keycloak integration passes: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`

**Dependencies:** Task 4

**Files likely touched:**

- `deploy/keycloak/infra-gate-realm.json`
- `tests/TestData/keycloak/infra-gate-realm.json`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

**Estimated scope:** Medium: 3 files

## Task 6: Decide and implement external MCP client compatibility behavior

**Description:** Determine whether the primary external MCP clients used with this repo can send DPoP proofs for OAuth-protected MCP requests. Then either require DPoP for `mcp-client`, or keep it in a constrained compatibility mode with explicit residual risk.

**Acceptance criteria:**

- [ ] The plan records which MCP clients can or cannot send `Authorization: DPoP` plus `DPoP` proof headers.
- [ ] If clients support DPoP, `mcp-client` requires DPoP-bound tokens in the Keycloak realm.
- [ ] If clients do not support DPoP, bearer compatibility is limited to the minimum scopes and documented as residual F-07 risk.

**Verification:**

- [ ] Relevant Keycloak integration test covers the chosen `mcp-client` behavior.
- [ ] Documentation is updated to describe the compatibility mode and its risk.

**Dependencies:** Task 5

**Files likely touched:**

- `deploy/keycloak/infra-gate-realm.json`
- `tests/TestData/keycloak/infra-gate-realm.json`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`
- `docs/MCP-compliance.md`
- `docs/mcp-clients-quirks.md`

**Estimated scope:** Medium: 5 files

## Checkpoint: Core Rollout

- [ ] Controlled service clients can obtain DPoP-bound tokens and call the Gateway.
- [ ] Local Keycloak integration tests pass with DPoP enforcement for controlled clients.
- [ ] External MCP client compatibility decision has been reviewed by a human.

### Phase 3: Hardening and Documentation

- [ ] Task 7: Remove avoidable token persistence from approval OAuth cookies
- [ ] Task 8: Add production replay-store decision and deployment guardrails
- [ ] Task 9: Update audit status and operational documentation

## Task 7: Remove avoidable token persistence from approval OAuth cookies

**Description:** The approval browser OAuth flow currently saves tokens through ASP.NET OAuth options. Since the Gateway only copies claims into the approval identity, avoid persisting the raw access token unless a test proves it is required.

**Acceptance criteria:**

- [ ] `oauthOptions.SaveTokens` is disabled or removed for the approval OAuth scheme.
- [ ] Approval login/callback tests still pass.
- [ ] A regression test confirms approval cookies do not contain a raw `access_token`.

**Verification:**

- [ ] Tests pass: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayAuthenticationTests`
- [ ] Keycloak approval callback coverage passes: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`

**Dependencies:** None

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

**Estimated scope:** Medium: 3 files

## Task 8: Add production replay-store decision and deployment guardrails

**Description:** Decide whether the initial in-memory proof replay store is acceptable for the supported deployment topology. If production can run multiple Gateway replicas, add a shared replay store before marking F-07 fully mitigated.

**Acceptance criteria:**

- [ ] Production mode fails startup or emits a clear configuration error if DPoP is required with an unsafe replay-store choice for the configured topology.
- [ ] The deployment docs state when in-memory proof replay storage is acceptable.
- [ ] If multi-replica Gateway is supported, a shared replay-store implementation is planned or implemented before F-07 status changes to mitigated.

**Verification:**

- [ ] Tests pass for runtime/configuration validation touched by the task.
- [ ] Build succeeds: `dotnet build InfraGate.slnx`

**Dependencies:** Task 3

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs`
- `src/InfraGate.McpGateway.Auth/Dpop/InMemoryDpopProofReplayStore.cs`
- `docs/configuration.md`
- `docs/architecture.md`

**Estimated scope:** Medium: 4 files

## Task 9: Update audit status and operational documentation

**Description:** Update the security-audit finding, MCP compliance docs, and setup/configuration docs so the implemented behavior is visible to future reviewers and operators.

**Acceptance criteria:**

- [ ] `.agents/Plans/loose/security-audit.md` marks F-07 according to actual completed scope: mitigated, partially mitigated, or still open for external client compatibility.
- [ ] Docs explain DPoP requirements, compatibility behavior, and residual risk.
- [ ] Any ADR-worthy decision, such as DPoP over mTLS or in-memory replay store limits, is captured or explicitly linked.

**Verification:**

- [ ] Documentation references match actual config names and tests.
- [ ] Relevant test README files list DPoP verification commands if new filters or categories are added.

**Dependencies:** Tasks 5, 6, 8

**Files likely touched:**

- `.agents/Plans/loose/security-audit.md`
- `docs/MCP-compliance.md`
- `docs/configuration.md`
- `docs/architecture.md`
- `tests/InfraGate.McpGateway.KeycloakTests/README.md`

**Estimated scope:** Medium: 5 files

## Checkpoint: Complete

- [ ] Gateway unit tests pass.
- [ ] Keycloak integration tests pass.
- [ ] `dotnet build InfraGate.slnx` succeeds.
- [ ] F-07 status in the audit matches the actual rollout state.
- [ ] Human review approves any residual bearer compatibility path.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| External MCP clients cannot produce DPoP proofs | High | Keep compatibility explicit, restrict scopes, keep 300-second token lifetime, and do not mark F-07 fully mitigated for those clients. |
| In-memory proof replay store is unsafe for multi-replica Gateway | High | Gate production configuration or add a shared replay store before production rollout. |
| Keycloak realm JSON attribute names are guessed incorrectly | Medium | Configure Keycloak 26.6.1 directly and export the realm; keep deploy/test realm equivalence tests. |
| DPoP validation logs sensitive token material | High | Add redaction tests and never include raw token/proof strings in validation results or logs. |
| DPoP work drifts into F-08, F-09, or downstream stdio auth | Medium | Keep plan ownership binding, revocation/introspection, and downstream transport sender constraints as separate findings unless explicitly reprioritized. |

## Parallelization Opportunities

- Tasks 1 and 7 can run independently because token persistence cleanup does not depend on DPoP validation.
- Task 4 can begin after Task 3 defines the Gateway request expectations.
- Task 9 documentation can start after architecture decisions are accepted, but final audit status must wait for verification.
- Tasks 5 and 6 need coordination because both modify Keycloak client behavior and realm JSON.

## Open Questions

- Which external MCP clients must work with this Gateway, and do they currently support DPoP proofs on OAuth-protected MCP calls?
- Is production expected to run more than one Gateway replica? If yes, what shared replay-store backend should be used for DPoP `jti` checks?
- Should `infra-gate-approval-ui` ever require DPoP, or is disabling saved tokens sufficient because that flow uses an approval cookie instead of using the access token as a Gateway API bearer credential?
- Should F-09 token revocation/introspection be scheduled immediately after F-07, or handled as an independent production-readiness item?

## Planning Verification

- [x] Every task has acceptance criteria.
- [x] Every task has a verification step.
- [x] Task dependencies are identified and ordered.
- [x] No task is expected to touch more than about five files.
- [x] Checkpoints exist between major phases.
- [ ] Human has reviewed and approved the plan before implementation starts.
