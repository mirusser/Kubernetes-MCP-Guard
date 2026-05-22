#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOCAL_ENV_FILE="${SONAR_LOCAL_ENV_FILE:-${REPO_ROOT}/.sonarqube-local/local.env}"

if [[ -f "${LOCAL_ENV_FILE}" ]]; then
  # shellcheck disable=SC1090
  source "${LOCAL_ENV_FILE}"
fi

command -v curl >/dev/null 2>&1 || {
  echo "curl is required." >&2
  exit 1
}

command -v jq >/dev/null 2>&1 || {
  echo "jq is required." >&2
  exit 1
}

if [[ -z "${SONAR_TOKEN:-}" ]]; then
  echo "Set SONAR_TOKEN to a local SonarQube analysis token." >&2
  exit 1
fi

PROJECT_KEY="${SONAR_PROJECT_KEY:-mirusser_Kubernetes-MCP-Guard}"
PROJECT_NAME="${SONAR_PROJECT_NAME:-Kubernetes MCP Guard}"
SONAR_URL="${SONAR_HOST_URL:-http://localhost:9000}"
CONFIGURATION="${CONFIGURATION:-Release}"
COVERAGE_RESULTS_DIR="TestResults/coverage"
OPENCOVER_REPORTS="${COVERAGE_RESULTS_DIR}/**/coverage.opencover.xml"
COVERAGE_EXCLUSIONS="**/Program.cs,**/PromptInjectionGuard.Regex.cs"
REPORT_PATH="${SONAR_REPORT_PATH:-.sonarqube-local/reports/sonarqube-local-report.json}"
METRICS="bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,sqale_rating,reliability_rating,security_rating,security_review_rating,software_quality_maintainability_rating,ncloc,complexity,cognitive_complexity"

wait_for_ce_task() {
  local ce_task_file=".sonarqube/out/.sonar/report-task.txt"
  local ce_task_id=""
  local status=""
  local attempt

  if [[ -f "${ce_task_file}" ]]; then
    ce_task_id="$(sed -n 's/^ceTaskId=//p' "${ce_task_file}" | head -n 1)"
  fi

  if [[ -z "${ce_task_id}" ]]; then
    echo "SonarQube CE task id not found; skipping CE wait." >&2
    return 0
  fi

  for attempt in {1..60}; do
    status="$(curl -sf -u "${SONAR_TOKEN}:" \
      "${SONAR_URL}/api/ce/task?id=${ce_task_id}" \
      | jq -r '.task.status // "MISSING"')"

    case "${status}" in
      SUCCESS)
        echo "SonarQube CE task ${ce_task_id} completed."
        return 0
        ;;
      FAILED|CANCELED)
        echo "SonarQube CE task ${ce_task_id} ended with ${status}." >&2
        return 1
        ;;
      *)
        echo "SonarQube CE task status: ${status}; waiting..."
        sleep 5
        ;;
    esac
  done

  echo "Timed out waiting for SonarQube CE task ${ce_task_id}." >&2
  return 1
}

fetch_paged_array() {
  local url="$1"
  local key="$2"
  local output_prefix="$3"
  local page=1
  local total=0
  local response

  rm -f "${output_prefix}"_*.json

  while true; do
    if ! response="$(curl -sf -u "${SONAR_TOKEN}:" "${url}&ps=500&p=${page}")"; then
      echo "Could not fetch ${key} from SonarQube; writing an empty ${key} array." >&2
      echo '[]'
      return 0
    fi
    jq ".${key} // []" <<< "${response}" > "${output_prefix}_${page}.json"
    total="$(jq -r '.paging.total // 0' <<< "${response}")"

    if (( page * 500 >= total )); then
      break
    fi

    page=$((page + 1))
  done

  jq -s 'add' "${output_prefix}"_*.json
}

export_report() {
  local report_dir
  local tmp_dir

  report_dir="$(dirname "${REPORT_PATH}")"
  tmp_dir="$(mktemp -d)"
  mkdir -p "${report_dir}"

  fetch_paged_array \
    "${SONAR_URL}/api/issues/search?componentKeys=${PROJECT_KEY}&additionalFields=rules" \
    "issues" \
    "${tmp_dir}/issues" \
    > "${tmp_dir}/issues-all.json"

  fetch_paged_array \
    "${SONAR_URL}/api/hotspots/search?projectKey=${PROJECT_KEY}" \
    "hotspots" \
    "${tmp_dir}/hotspots" \
    > "${tmp_dir}/hotspots-all.json"

  curl -sf -u "${SONAR_TOKEN}:" \
    "${SONAR_URL}/api/qualitygates/project_status?projectKey=${PROJECT_KEY}" \
    > "${tmp_dir}/quality-gate.json" || {
      echo "Could not fetch quality gate from SonarQube; writing an empty qualityGate object." >&2
      echo '{}' > "${tmp_dir}/quality-gate.json"
    }

  curl -sf -u "${SONAR_TOKEN}:" \
    "${SONAR_URL}/api/measures/component?component=${PROJECT_KEY}&metricKeys=${METRICS}" \
    > "${tmp_dir}/measures.json" || {
      echo "Could not fetch measures from SonarQube; writing an empty measures object." >&2
      echo '{}' > "${tmp_dir}/measures.json"
    }

  jq -n \
    --arg project "${PROJECT_KEY}" \
    --arg generatedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg sonarUrl "${SONAR_URL}" \
    --arg dashboardUrl "${SONAR_URL}/dashboard?id=${PROJECT_KEY}" \
    --slurpfile issues "${tmp_dir}/issues-all.json" \
    --slurpfile hotspots "${tmp_dir}/hotspots-all.json" \
    --slurpfile qualityGate "${tmp_dir}/quality-gate.json" \
    --slurpfile measures "${tmp_dir}/measures.json" \
    '{
      metadata: {
        generatedAt: $generatedAt,
        projectKey: $project,
        sonarUrl: $sonarUrl,
        dashboardUrl: $dashboardUrl,
        source: "local-sonarqube"
      },
      qualityGate: $qualityGate[0],
      measures: $measures[0],
      issues: $issues[0],
      hotspots: $hotspots[0]
    }' > "${REPORT_PATH}"

  rm -rf "${tmp_dir}"
  echo "Wrote SonarQube report to ${REPORT_PATH}."
}

cd "${REPO_ROOT}"

echo "Cleaning old OpenCover output..."
rm -rf "${COVERAGE_RESULTS_DIR}"

echo "Restoring local .NET tools..."
dotnet tool restore

echo "Starting SonarQube analysis for ${PROJECT_KEY} at ${SONAR_URL}..."
dotnet tool run dotnet-sonarscanner begin \
  /k:"${PROJECT_KEY}" \
  /n:"${PROJECT_NAME}" \
  /d:sonar.token="${SONAR_TOKEN}" \
  /d:sonar.host.url="${SONAR_URL}" \
  /d:sonar.coverage.exclusions="${COVERAGE_EXCLUSIONS}" \
  /d:sonar.cs.opencover.reportsPaths="${OPENCOVER_REPORTS}"

echo "Restoring NuGet packages..."
dotnet restore InfraGate.slnx

echo "Building solution..."
dotnet build InfraGate.slnx --configuration "${CONFIGURATION}" --no-restore

echo "Running tests with OpenCover coverage..."
dotnet test InfraGate.slnx \
  --configuration "${CONFIGURATION}" \
  --no-build \
  --filter "Category!=Keycloak" \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory "${COVERAGE_RESULTS_DIR}" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

shopt -s globstar nullglob
coverage_reports=(${OPENCOVER_REPORTS})
if (( ${#coverage_reports[@]} == 0 )); then
  echo "No OpenCover report found under ${COVERAGE_RESULTS_DIR}." >&2
  exit 1
fi

echo "Found ${#coverage_reports[@]} OpenCover report(s)."

echo "Uploading analysis to SonarQube..."
dotnet tool run dotnet-sonarscanner end /d:sonar.token="${SONAR_TOKEN}"

wait_for_ce_task
export_report

echo "SonarQube analysis completed."
