# SonarCloud CI Integration

## Summary
- Integrate SonarCloud (free SaaS) into CI as a standalone GitHub Actions workflow (`sonar.yml`).
- No self-hosted server — SonarCloud provides PR decoration, quality gates, code smells, and coverage tracking.
- Existing workflows (`dotnet-build.yml`, `unit-tests.yml`, `docker.yml`) remain untouched.
- Uses `dotnet-coverage` for Sonar-compatible coverage (VS coverage XML format).
- Existing `coverlet.collector` + `reportgenerator` stays untouched for local `coverage.sh`.

## Pre-Requisites (Outside Repo)
1. Sign up at [sonarcloud.io](https://sonarcloud.io) (GitHub OAuth is fastest).
2. Create a SonarCloud organization (e.g., `mirusser`).
3. Create a project for this repo, e.g., key: `mirusser_k8s-toolkit`.
4. Generate a token: My Account → Security → Generate Token → copy.
5. Add repo secrets/vars in GitHub:
   - Secret: `SONAR_TOKEN` — the token from step 4.
   - Variable: `SONAR_ORGANIZATION` — the org name from step 2.

## Key Changes

### 1. Add `dotnet-sonarscanner` and `dotnet-coverage` local tools
- File: `.config/dotnet-tools.json`
- Add `dotnet-sonarscanner` v11.2.1 and `dotnet-coverage` v18.6.2 alongside existing `reportgenerator` entry.
- Follows existing pattern — the manifest already has one tool.
- `dotnet-coverage` is Microsoft's cross-platform coverage tool; produces VS coverage XML, the recommended format for SonarScanner.

### 2. Create `sonar.yml` workflow
- File: `.github/workflows/sonar.yml` (new)
- Triggers: `pull_request` to `main`, `push` to `main`, `workflow_dispatch`.
- Runs on `ubuntu-latest` with full git history (`fetch-depth: 0` required by Sonar).
- Steps:
  1. Checkout (`fetch-depth: 0`)
  2. Setup .NET 10.0.x
  3. `dotnet tool restore` (restores sonarscanner + dotnet-coverage + reportgenerator)
  4. `dotnet sonarscanner begin` with:
     - `/k:"${{ vars.SONAR_PROJECT_KEY }}"` project key
     - `/o:"${{ vars.SONAR_ORGANIZATION }}"`
     - `/d:sonar.token="${{ secrets.SONAR_TOKEN }}"`
     - `/d:sonar.host.url="https://sonarcloud.io"`
     - `/d:sonar.cs.vscoveragexml.reportsPaths="TestResults/coverage.xml"`
  5. `dotnet restore InfraGate.slnx`
  6. `dotnet build InfraGate.slnx --configuration Release --no-restore`
  7. `dotnet tool run dotnet-coverage collect "dotnet test InfraGate.slnx --configuration Release --no-build" -f xml -o TestResults/coverage.xml`
  8. `dotnet sonarscanner end /d:sonar.token="${{ secrets.SONAR_TOKEN }}"`
- Optional: Add `qualitygate.wait=true` to the `begin` step properties so the workflow waits for the quality gate result.

### 3. Coverage format: dotnet-coverage (not coverlet)
- `dotnet-coverage` produces VS coverage XML, read by SonarScanner via `sonar.cs.vscoveragexml.reportsPaths`.
- The OpenCover path was abandoned because ReportGenerator's OpenCover output requires a paid license.
- Existing `coverlet.runsettings` and `coverage.sh` remain unchanged for local dev.

### 4. Update `.gitignore`
- Add `.sonarqube/` and `TestResults/` (the latter may already be there — verify).

## What Stays Unchanged
- `dotnet-build.yml` — untouched
- `unit-tests.yml` — untouched (remains fast, no coverage overhead)
- `docker.yml` — untouched
- `integration-tests.yml` — untouched
- `scripts/coverage.sh` — untouched (local dev coverage check)
- All `.csproj` files — untouched
- `coverlet.runsettings` — untouched (Cobertura still works for local)

## Quality Gate (SonarCloud UI, Not In Code)
- Default "Sonar way" quality profile for C#.
- Default built-in quality gate: no new bugs/vulnerabilities/code smells on new code.
- Coverage tracked per-analysis; history builds over time.

## Verification
1. Push the branch with these changes → PR opened.
2. SonarCloud analysis runs in the `sonar.yml` workflow.
3. SonarCloud bot comments on the PR with analysis summary.
4. Quality gate status visible on the PR checks panel.
5. Dashboard at `https://sonarcloud.io/project/overview?id={org}_{repo}`.
