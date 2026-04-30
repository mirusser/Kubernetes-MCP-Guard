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
