#!/usr/bin/env bash
set -euo pipefail

coverage_threshold=80
merged_cobertura="coverage-report/Cobertura.xml"

echo "Cleaning old coverage output..."
rm -rf TestResults/coverage coverage-report

echo "Restoring local .NET tools..."
dotnet tool restore

echo "Running tests with code coverage..."
dotnet test InfraGate.slnx \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory TestResults/coverage

echo "Generating HTML and merged Cobertura reports..."
dotnet tool run reportgenerator \
  -reports:"TestResults/coverage/**/coverage.cobertura.xml" \
  -targetdir:"coverage-report" \
  -reporttypes:"Html;Cobertura"

if [[ ! -f "${merged_cobertura}" ]]; then
  echo "Merged Cobertura report was not generated: ${merged_cobertura}" >&2
  exit 1
fi

line_rate="$(sed -n 's/.*line-rate="\([^"]*\)".*/\1/p' "${merged_cobertura}" | head -n 1)"
branch_rate="$(sed -n 's/.*branch-rate="\([^"]*\)".*/\1/p' "${merged_cobertura}" | head -n 1)"

if [[ -z "${line_rate}" || -z "${branch_rate}" ]]; then
  echo "Could not read coverage rates from ${merged_cobertura}" >&2
  exit 1
fi

line_percent="$(awk -v rate="${line_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
branch_percent="$(awk -v rate="${branch_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"

echo "Line coverage: ${line_percent}%"
echo "Branch coverage: ${branch_percent}%"
echo "Coverage report generated at: coverage-report/index.html"

if ! awk -v rate="${line_rate}" -v threshold="${coverage_threshold}" \
  'BEGIN { exit(rate * 100 >= threshold ? 0 : 1) }'; then
  echo "Line coverage ${line_percent}% is below required ${coverage_threshold}%." >&2
  exit 1
fi
