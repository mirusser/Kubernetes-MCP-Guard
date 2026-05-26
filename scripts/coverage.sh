#!/usr/bin/env bash
set -euo pipefail

line_coverage_threshold=80
# Branch coverage baseline raised from 63% to 69% on 2026-05-17.
# The remaining gap (~150 branches) is concentrated in Kubernetes-dependent code
# (KubernetesManager.Evidence, Execute, DryRun, Apply — all 0% covered).
# Raise to 80 after adding integration tests with a mock Kubernetes client for those.
branch_coverage_threshold=69
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

line_rate="$(awk 'match($0, /line-rate="[^"]+"/) { value = substr($0, RSTART + 11, RLENGTH - 12); print value; exit }' "${merged_cobertura}")"
branch_rate="$(awk 'match($0, /branch-rate="[^"]+"/) { value = substr($0, RSTART + 13, RLENGTH - 14); print value; exit }' "${merged_cobertura}")"

if [[ -z "${line_rate}" || -z "${branch_rate}" ]]; then
  echo "Could not read coverage rates from ${merged_cobertura}" >&2
  exit 1
fi

line_percent="$(awk -v rate="${line_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
branch_percent="$(awk -v rate="${branch_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"

echo "Line coverage: ${line_percent}%"
echo "Branch coverage: ${branch_percent}%"
echo "Coverage report generated at: coverage-report/index.html"

if ! awk -v rate="${line_rate}" -v threshold="${line_coverage_threshold}" \
  'BEGIN { exit(rate * 100 >= threshold ? 0 : 1) }'; then
  echo "Line coverage ${line_percent}% is below required ${line_coverage_threshold}%." >&2
  exit 1
fi

if ! awk -v rate="${branch_rate}" -v threshold="${branch_coverage_threshold}" \
  'BEGIN { exit(rate * 100 >= threshold ? 0 : 1) }'; then
  echo "Branch coverage ${branch_percent}% is below required ${branch_coverage_threshold}%." >&2
  exit 1
fi
