#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ACT_STATE_DIR="$REPO_ROOT/.act"
ACT_IMAGE="${ACT_IMAGE:-nektos/act:latest}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

usage() {
    cat <<EOF
Usage: $(basename "$0") [options] [job]

Run GitHub Actions workflows locally with act inside Docker.
No local act install needed — only Docker.

Jobs (from .github/workflows/):
  lint-and-test    Run format check, build, and tests (default)
  docker           Build Docker images (requires secrets)
  sonar            Run SonarCloud analysis (requires secrets)

Options:
  -w, --workflow FILE   Workflow file (default: ci.yml)
  -p, --pull            Pull latest act runner image (default: false)
  -i, --image TAG       Act Docker image tag (default: nektos/act:latest)
  -l, --list-jobs       List available jobs and exit
  -h, --help            Show this help

Examples:
  $(basename "$0")                          # Run lint-and-test from ci.yml
  $(basename "$0") lint-and-test            # Same, explicit job name
  $(basename "$0") -w publish.yml docker    # Run docker job from publish.yml
  $(basename "$0") --list-jobs              # List all available jobs
EOF
    exit 0
}

LIST_JOBS=false
WORKFLOW="ci.yml"
PULL=false
JOB=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        -w|--workflow) WORKFLOW="$2"; shift 2 ;;
        -p|--pull) PULL=true; shift ;;
        -i|--image) ACT_IMAGE="$2"; shift 2 ;;
        -l|--list-jobs) LIST_JOBS=true; shift ;;
        -h|--help) usage ;;
        -*)
            echo -e "${RED}Unknown option: $1${NC}" >&2
            usage
            ;;
        *)
            JOB="$1"
            shift
            ;;
    esac
done

check_prerequisites() {
    if ! docker info &>/dev/null; then
        echo -e "${RED}Error: Docker is not running or not accessible.${NC}" >&2
        exit 1
    fi

    # Pull the act image if needed
    if [[ "$PULL" == true ]] || ! docker image inspect "$ACT_IMAGE" &>/dev/null; then
        echo -e "${CYAN}Pulling act image: $ACT_IMAGE${NC}"
        docker pull --quiet "$ACT_IMAGE"
    fi
}

docker_act() {
    local act_args=("$@")

    mkdir -p "$ACT_STATE_DIR"

    # Mounts:
    #   /var/run/docker.sock  — so act can spawn sibling containers for each step
    #   repo root -> /workspace — the code to run against
    #   .act/ -> /workspace/.act — act cache (runner downloads, etc.)
    docker run --rm -it \
        -v /var/run/docker.sock:/var/run/docker.sock \
        -v "$REPO_ROOT:/workspace" \
        -v "$ACT_STATE_DIR:/workspace/.act" \
        -w /workspace \
        "$ACT_IMAGE" \
        "${act_args[@]}"
}

list_jobs() {
    local workflow="$1"
    local workflow_path=".github/workflows/$workflow"

    if [[ ! -f "$REPO_ROOT/$workflow_path" ]]; then
        echo -e "${RED}Workflow not found: $workflow_path${NC}" >&2
        exit 1
    fi

    echo "Jobs in $workflow_path:"
    docker_act -l -W "$workflow_path" 2>/dev/null || \
        grep -E '^\s+[a-zA-Z0-9_-]+:' "$REPO_ROOT/$workflow_path" | \
        sed 's/://' | head -n -1
}

setup_secrets_file() {
    local secrets_file="$ACT_STATE_DIR/secrets.env"
    mkdir -p "$ACT_STATE_DIR"

    if [[ "$WORKFLOW" == "publish.yml" ]]; then
        if [[ ! -f "$secrets_file" ]]; then
            echo -e "${YELLOW}Creating placeholder secrets file: $secrets_file${NC}"
            cat > "$secrets_file" <<-EOF
# Secrets for local act runs.
# Replace placeholder values with real ones if needed.
DOCKERHUB_TOKEN=
GITHUB_TOKEN=
EOF
            chmod 600 "$secrets_file"
            echo -e "${YELLOW}Edit $secrets_file with actual values before running.${NC}"
        fi
        echo "$secrets_file"
    fi
}

setup_vars_file() {
    local vars_file="$ACT_STATE_DIR/vars.env"
    mkdir -p "$ACT_STATE_DIR"

    if [[ "$WORKFLOW" == "publish.yml" ]] && [[ ! -f "$vars_file" ]]; then
        cat > "$vars_file" <<-EOF
# GitHub variables for local act runs.
DOCKERHUB_USERNAME=
DOCKERHUB_NAMESPACE=local
DEPLOY_PATH=/opt/infra-gate
EOF
    fi

    if [[ -f "$vars_file" ]]; then
        echo "$vars_file"
    fi
}

run_act() {
    local job="$1"
    local workflow_path=".github/workflows/$WORKFLOW"

    if [[ ! -f "$REPO_ROOT/$workflow_path" ]]; then
        echo -e "${RED}Workflow not found: $workflow_path${NC}" >&2
        exit 1
    fi

    local act_args=(
        -W "$workflow_path"
        --env "FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true"
    )

    if [[ "$PULL" == false ]]; then
        act_args+=(--pull=false)
    fi

    if [[ -n "$job" ]]; then
        act_args+=(-j "$job")
    fi

    local secrets_file
    secrets_file="$(setup_secrets_file)"
    if [[ -n "$secrets_file" ]]; then
        # Mount the secrets file into the container
        local container_secrets="/tmp/act-secrets.env"
        act_args+=(
            -v "$secrets_file:$container_secrets"
            --secret-file "$container_secrets"
        )
    fi

    local vars_file
    vars_file="$(setup_vars_file)"
    if [[ -n "$vars_file" ]]; then
        local container_vars="/tmp/act-vars.env"
        act_args+=(
            -v "$vars_file:$container_vars"
            --var-file "$container_vars"
        )
    fi

    echo -e "${GREEN}Running act (Docker container):${NC}"
    echo "  Image: $ACT_IMAGE"
    echo "  Workflow: $workflow_path"
    echo "  Job: ${job:-<default>}"
    echo ""

    docker_act "${act_args[@]}"
}

check_prerequisites

if [[ "$LIST_JOBS" == true ]]; then
    list_jobs "$WORKFLOW"
    exit 0
fi

if [[ -z "$JOB" ]]; then
    JOB="lint-and-test"
fi

run_act "$JOB"
