# Code Coverage Reporting Setup

This plan implements a code coverage reporting workflow based on your provided simple setup using `coverlet.collector` and `ReportGenerator`.

## Proposed Changes

### .NET Tool Configuration
Initialize the local .NET tool manifest and install `dotnet-reportgenerator-globaltool` to ensure consistency across the project.

#### [NEW] .config/dotnet-tools.json
Will be created by running:
```bash
dotnet new tool-manifest
dotnet tool install dotnet-reportgenerator-globaltool
```

### Git Ignore
Ignore generated coverage output so reports do not pollute commits.

#### [MODIFY] .gitignore
Add:
```gitignore
coverage-report/
```

`TestResults/` is already ignored and will continue to hold the raw Cobertura files.

### Scripts
Create a script to run tests and generate the HTML report.

#### [NEW] scripts/coverage.sh
A bash script to automate test execution and report generation. It should restore local tools, remove stale coverage outputs, run the full solution with coverage, and point ReportGenerator only at the fresh coverage results.

```bash
#!/usr/bin/env bash
set -euo pipefail

echo "Cleaning old coverage output..."
rm -rf TestResults/coverage coverage-report

echo "Restoring local .NET tools..."
dotnet tool restore

echo "Running tests with code coverage..."
dotnet test InfraGate.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults/coverage

echo "Generating HTML report..."
dotnet tool run reportgenerator \
  -reports:"TestResults/coverage/**/coverage.cobertura.xml" \
  -targetdir:"coverage-report" \
  -reporttypes:Html

echo "Coverage report generated at: coverage-report/index.html"
```
The script will be made executable with `chmod +x scripts/coverage.sh`.

### Environment Setup Documentation
Update the `.agents/environment-setup.md` file to document the installation of .NET tools and their usage for code coverage.

#### [MODIFY] .agents/environment-setup.md
Add a new row to the tools table for `.NET Local Tools (ReportGenerator)` and a section outlining the code coverage reporting approach, including the `dotnet tool restore` command.

### Human-Facing Documentation
Add a short coverage command note to the developer docs so humans do not have to discover this through `.agents` files.

#### [MODIFY] docs/devs-readme.md
Add `./scripts/coverage.sh` to the verification section, with a note that the HTML report is generated at `coverage-report/index.html`.

## Verification Plan

### Automated Tests
- Run `./scripts/coverage.sh`.
- Verify that `coverage-report/index.html` is generated.
- Verify the script creates fresh Cobertura files under `TestResults/coverage/`.
- Check that the output reflects coverage from all existing test projects, which already reference `coverlet.collector`.
- Confirm `git status --short` does not show generated `TestResults/coverage/` or `coverage-report/` files.
