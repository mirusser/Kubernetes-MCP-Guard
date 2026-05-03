# Coverage To 80%+ And First Test Cleanup

## Summary
- Current raw coverage: `77.4%` lines, `63.4%` branches; all 97 tests pass.
- Coverage report is available at `coverage-report/index.html`.
- Use “meaningful coverage” as agreed: exclude compiler-generated regex matcher files and thin `Program.cs` startup glue from the coverage denominator.
- Temporary validation showed that policy raises coverage to `80.0%` (`2005/2504`), but first pass should add real tests to create buffer above the threshold.

## Key Changes
- Add root `coverlet.runsettings` for `XPlat Code Coverage`:
  - `ExcludeByFile`: `**/System.Text.RegularExpressions.Generator/**/*.cs,**/Program.cs`
  - Keep Cobertura output.
- Update `scripts/coverage.sh`:
  - Run `dotnet test InfraGate.slnx --collect:"XPlat Code Coverage" --settings coverlet.runsettings`.
  - Generate both HTML and merged Cobertura via ReportGenerator.
  - Fail if merged line coverage is below `80%`.
  - Print the final line/branch coverage and report path.
- Access plan:
  - Browser file path: `coverage-report/index.html`
  - Headless/local HTTP option: `python3 -m http.server 8080 -d coverage-report`, then open `http://127.0.0.1:8080/`.

## First-Pass Tests And Cleanup
- Add focused tests for real uncovered logic:
  - `DevIssuerOptions.FromEnvironment`: defaults and env override behavior.
  - `K8sMcpOptions.FromEnvironment`: default approval root, configured approval root, configured namespaces.
  - `PromptInjectionGuard.ScanArguments`: nested `JsonElement`, `JsonNode`, arrays, non-generic dictionaries, and remaining guardrail categories.
  - `K8sManager.ApplyApprovedPlanAsync`: scale patch, restart patch, delete plan including `404` “already absent”, and apply manifest object mismatch.
- Reduce test convolution:
  - Extract duplicated `TestKubernetesApi`, `CapturedRequest`, and `TestResponse` from K8s manager test files into one shared test helper.
  - Keep production `K8sManager` behavior unchanged in this pass; use the new tests to make later refactoring safer.

## Test Plan
- Run `dotnet test InfraGate.slnx`.
- Run `./scripts/coverage.sh`.
- Acceptance:
  - All tests pass.
  - `coverage.sh` exits non-zero below `80%`.
  - Report opens at `coverage-report/index.html`.
  - Expected post-pass line coverage target: at least `82%` to avoid sitting exactly on the gate.

## Assumptions
- Integration tests remain opt-in and are not required for normal coverage.
- The 80% target applies to meaningful production/testable code after excluding generated regex and startup glue.
- No public runtime APIs or MCP tool contracts should change in this first pass.
