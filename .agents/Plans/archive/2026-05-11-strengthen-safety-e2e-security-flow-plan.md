# Plan: Strengthen Safety E2E Security-Flow Coverage

## Summary
Upgrade `InfraGate.Safety.E2E.Tests` from mostly core server/storage safety checks into a small set of true gateway-flow checks. Keep the existing focused safety tests, but add shared helpers and vertical workflows that exercise HTTP MCP with bearer auth, the gateway tool facade, approval URL creation, approval page auth/antiforgery, browser approval POST, final MCP apply, and Kubernetes mutation/refusal.

Fully automating real Keycloak browser login is possible but brittle because it requires scraping Keycloak HTML login forms. The recommended test boundary is real Keycloak JWTs for MCP calls, plus simulated approval OAuth callback/cookie identity for browser approval endpoints, matching the gateway integration-test pattern.

## Tasks

1. Add Safety E2E HTTP MCP helpers.
   - Create a real HTTP MCP client against `/mcp` using `AcquireTokenAsync()`.
   - Call MCP tools through HTTP instead of `DownstreamClient`.
   - Parse `PlanId`, approval URL challenge id, and antiforgery token.
   - Create an authenticated approval browser by driving the approval OAuth callback/cookie flow with a test OAuth backchannel subject.

2. Add one happy-path full approval flow.
   - Request a restart through HTTP MCP.
   - Call `apply_approved_plan` through HTTP MCP and assert it returns an approval URL without mutating.
   - Assert unauthenticated browser GET redirects to approval login.
   - Render the authenticated approval page and assert dry-run/diff evidence.
   - POST approval with antiforgery.
   - Call `apply_approved_plan` again through HTTP MCP and assert Kubernetes mutation plus durable audit/file evidence.

3. Upgrade direct-approval tamper tests where feasible.
   - `PlanHashMismatchTests`: approve via browser endpoint, mutate pending plan, then apply through HTTP MCP and expect stale approval not to be accepted.
   - `AlreadyAppliedPlanTests`: approve via browser endpoint, apply once through HTTP MCP, then apply a second time through HTTP MCP and expect refusal.
   - Keep direct file mutation only as attack setup.

4. Strengthen wrong-user approval coverage.
   - Create challenge as requester subject A.
   - Try POST `/approve` as subject B through authenticated approval browser.
   - Assert no approved hash is written, challenge is not approved, and rejection audit is written.
   - Keep the direct service-level wrong-user test as defense-in-depth.

5. Add gateway-path negative tests for request-time gates.
   - `request_apply_manifest` with a privileged container through HTTP MCP returns policy refusal and creates no pending plan.
   - `request_apply_manifest` with negative replicas through HTTP MCP returns dry-run refusal and creates no pending plan.
   - Use HTTP MCP and browser approval for the apply-time dry-run refusal where possible.

6. Update documentation to match the new coverage.
   - Document which tests are full HTTP/browser/Kubernetes flow.
   - Document which tests are focused lower-level safety probes.
   - State that approval OAuth is callback-simulated in tests unless real Keycloak browser login is later added.
   - Keep the unset-env behavior wording: tests pass quickly without exercising the live flow.

## Limits
- Do not automate real Keycloak browser-login HTML scraping in this pass.
- Real Keycloak remains used for MCP bearer-token coverage.
- Approval browser identity is simulated at the OAuth callback/cookie boundary.
- Existing server/storage safety tests remain as defense-in-depth.

## Verification
- `dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --filter "Category=SafetyE2E"`
- `INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG="$(pwd)/.kube/mcp-nginx-demo.config" dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --no-build --filter "FullyQualifiedName~FullApprovalFlow"`
- `INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG="$(pwd)/.kube/mcp-nginx-demo.config" dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --no-build --filter "Category=SafetyE2E"`
- `git diff --check`
