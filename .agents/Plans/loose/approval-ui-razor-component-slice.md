# Approval UI Razor Component Slice

## Summary

Build Option A: a small Razor component library rendered by the existing gateway endpoints, with boundaries chosen so the same components/models can later become a routed Blazor Web App. The Kubernetes adapter will stop owning HTML and will expose review data through `IPlanReview`/`KubernetesPlan`; the gateway will own HTTP/auth/antiforgery and call an approval UI renderer.

Docs basis: Microsoft documents Razor components as Blazor UI units renderable to HTML strings with `HtmlRenderer`, and Razor class libraries as the reusable UI packaging shape.

## Key Boundaries And Interfaces

- `InfraGate.Approvals` owns generic review data contracts:
  - Extend `IPlanReview` with non-HTML summary data, likely `string Description` and `IReadOnlyList<PlanReviewTarget> Targets`.
  - Add `PlanReviewTarget(string Type, string Name, string? Scope, IReadOnlyDictionary<string,string> Attributes)`.
  - Keep `HasReviewEvidence` and `CanBeApproved` as generic approval-readiness signals.
- `InfraGate.KubernetesAdapter` owns Kubernetes review data only:
  - `KubernetesPlan` remains the domain data model with manifest, dry-run, diffs, policy findings, and objects.
  - It maps Kubernetes objects to generic `PlanReviewTarget` values.
  - Remove `KubernetesPlanReviewRenderer` and remove `IPlanReviewRenderer` from the domain adapter seam.
- `InfraGate.ApprovalUi` owns browser rendering:
  - New Razor class library using `.razor` components and an async `IApprovalPageRenderer`.
  - UI models are renderer-owned and gateway-neutral: approval page state, action URLs, antiforgery token name/value, decision result, and `IPlanReview`.
  - The v1 review component type-switches on `KubernetesPlan` in one place; future adapters add one renderer mapping without changing gateway endpoint logic.
- `InfraGate.McpGateway` owns transport:
  - Keeps `/approvals/*`, OAuth cookie auth, antiforgery validation, and POST decision endpoints.
  - Builds UI view models and calls `IApprovalPageRenderer`.
  - Moves MCP approval-required text to a gateway-owned formatter using generic `IPlanReview` summary/targets.

## Task Breakdown

### Task 1: Introduce Review Data Contracts

Description: Add the minimum generic data needed by both browser UI and MCP approval-required text without creating a generic review-document DSL.

Acceptance criteria:
- `IPlanReview` exposes summary data and targets without HTML or CSS concepts.
- `KubernetesPlan` implements the new members from existing payload data.
- Test fakes compile without adding adapter rendering dependencies.

Verification:
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter KubernetesPlanReviewTests`
- `dotnet build InfraGate.slnx`

Dependencies: None

Estimated scope: Medium

### Task 2: Remove Adapter-Owned HTML Rendering

Description: Delete the HTML renderer seam from `InfraGate.Approvals`/`InfraGate.KubernetesAdapter` and replace approval-required text generation with a gateway-owned formatter.

Acceptance criteria:
- `IDomainAdapter` no longer inherits or delegates `IPlanReviewRenderer`.
- `KubernetesPlanReviewRenderer` is removed.
- `GatewayApprovalService` still returns approval-required text with plan id, operation, targets, intent digest, review digest, approval URL, expiry, and polling instruction.

Verification:
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayApprovalServiceTests`
- Update renderer tests into data/formatter tests rather than HTML tests.

Dependencies: Task 1

Estimated scope: Medium

### Task 3: Add Razor Component Library

Description: Add `src/InfraGate.ApprovalUi` as a Razor class library for static approval page rendering.

Acceptance criteria:
- Project is added to `InfraGate.slnx`.
- Defines `IApprovalPageRenderer` with async methods such as `RenderApprovalPageAsync` and `RenderDecisionPageAsync`.
- Defines gateway-neutral view models for approval page, actions, unavailable state, decision result, and plan review data.
- Uses `HtmlRenderer` internally; endpoints receive plain HTML output.

Verification:
- `dotnet build src/InfraGate.ApprovalUi/InfraGate.ApprovalUi.csproj`
- `dotnet build InfraGate.slnx`

Dependencies: Task 1

Estimated scope: Medium

### Task 4: Build Approval UI Components

Description: Move page shell, summary, actions, unavailable page, decision page, and Kubernetes evidence rendering into `.razor` components.

Acceptance criteria:
- Components preserve existing semantic `data-section`, `data-field`, and `data-action` attributes.
- Components HTML-encode user-controlled values through normal Razor rendering.
- Kubernetes evidence renders objects, optional submitted manifest, policy findings, dry-run, and diffs from `KubernetesPlan`.
- Unknown `IPlanReview` types render a clear unsupported-evidence section while still showing generic summary/actions.

Verification:
- Add `tests/InfraGate.ApprovalUi.Tests` for component renderer output.
- Focus assertions on semantic attributes and stable data values, not prose or CSS.

Dependencies: Task 3

Estimated scope: Medium

### Task 5: Wire Gateway Endpoints To UI Renderer

Description: Replace `GatewayApprovalEndpoints` string-building methods with calls to the UI renderer while keeping endpoint behavior intact.

Acceptance criteria:
- GET `/approvals/{challengeId}` still loads page data, stores antiforgery tokens, and returns `text/html; charset=utf-8`.
- POST approve/deny/cancel still validates antiforgery before mutating challenge state.
- Gateway supplies action URLs and token field names to the UI model; the UI project does not depend on gateway conventions.
- Existing auth and Keycloak/TestHost setup remain unchanged except DI registration for approval UI.

Verification:
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` if Docker is available.

Dependencies: Tasks 3 and 4

Estimated scope: Medium

### Task 6: Update Documentation

Description: Refresh README claims around who owns approval rendering and what the adapter returns.

Acceptance criteria:
- `src/InfraGate.McpGateway/README.md` says the gateway hosts the Review Surface and delegates rendering to `InfraGate.ApprovalUi`.
- `src/InfraGate.KubernetesAdapter/README.md` says the adapter supplies review data/evidence, not HTML.
- Test README mentions the new approval UI test project if added.

Verification:
- `git diff --check`
- `rg -n "KubernetesPlanReviewRenderer|IPlanReviewRenderer|Review HTML rendered by this adapter" README.md docs src tests`

Dependencies: Tasks 2-5

Estimated scope: Small

## Future Blazor App Route

- Keep approval UI components non-routable in v1: no `@page`, no render modes, no client interactivity.
- Keep page data in explicit view models instead of reading `HttpContext` from components.
- Later expansion can add routed components and `MapRazorComponents` in the gateway, reusing the same components and view models.
- If more domain adapters appear, split the Kubernetes-specific component into an adapter-specific UI RCL and resolve evidence components by `AdapterId`.

## Test Plan

- New UI tests: static approval page, unavailable page, decision page, Kubernetes evidence sections, encoding of special characters, approve disabled when `CanBeApproved` is false.
- Gateway tests: endpoint token/action URL wiring, POST antiforgery behavior unchanged, approval service message formatter output.
- Adapter tests: `KubernetesPlan` data and evidence readiness only; no HTML assertions.
- Full check: `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`.

## Assumptions

- First slice remains static server-rendered HTML; no SignalR, WebAssembly, JavaScript, or client-side Blazor interactivity.
- Existing semantic attributes are part of the test/tooling contract and must stay stable.
- Review digest semantics are not redesigned in this slice; review data remains derived from the stored envelope/payload and existing evidence artifact summaries.
- The v1 UI project may reference `InfraGate.KubernetesAdapter` because Kubernetes is the only adapter today; the plan isolates that reference so it can be split later.
