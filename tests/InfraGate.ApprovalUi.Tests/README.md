# InfraGate.ApprovalUi.Tests

`InfraGate.ApprovalUi.Tests` covers the Razor component library for static approval page rendering. It verifies that the `ApprovalPageRenderer` produces correct semantic HTML with the expected `data-section`, `data-field`, and `data-action` attributes.

## What It Covers

- `ApprovalPageRendererTests.cs`: full approval page with plan summary and actions, Kubernetes evidence sections, unavailable-page error and fallback text, decision-page success/failure states, approve-button disable behavior, empty-diffs and non-Kubernetes plan support, and null-challenge/plan-review edge cases.

## Running Tests

- `dotnet test tests/InfraGate.ApprovalUi.Tests/InfraGate.ApprovalUi.Tests.csproj`

No integration or external dependencies.
