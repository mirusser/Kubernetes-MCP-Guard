# Local GitHub Actions Runner (act)

Run GitHub Actions workflows locally in Docker using [nektos/act](https://github.com/nektos/act) — no local `act` binary needed, only Docker.

Catches failures like formatting errors, build breaks, or permission issues before they reach CI.

## Prerequisites

- Docker (running)

## Quick Start

```bash
# Run the default CI lint-and-test job (format check, build, tests)
./tools/act/run-local.sh
```

This runs the `lint-and-test` job from `.github/workflows/ci.yml` — the same `dotnet format --verify-no-changes`, build, and test steps that run on every PR.

On first run it pulls the `nektos/act:latest` image automatically.

## Usage

```bash
./tools/act/run-local.sh [options] [job]

Jobs:
  lint-and-test    Run format check, build, and tests (default)
  docker           Build Docker images (requires secrets; see below)
  sonar            Run SonarCloud analysis (requires secrets)

Options:
  -w, --workflow FILE   Workflow file (default: ci.yml)
  -p, --pull            Pull latest act runner image
  -i, --image TAG       Act Docker image tag (default: nektos/act:latest)
  -l, --list-jobs       List available jobs and exit
  -h, --help            Show this help
```

### Run a specific job

```bash
# Run the Docker build job from publish.yml
./tools/act/run-local.sh -w publish.yml docker

# List jobs in a workflow
./tools/act/run-local.sh -w publish.yml --list-jobs
```

### Secrets and Variables

The script creates placeholder files under `.act/` (gitignored) for workflows that need them. For `publish.yml`, edit `.act/secrets.env` and `.act/vars.env` with appropriate values before running.

## What Gets Checked

| Job | Checks |
|-----|--------|
| `lint-and-test` | `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`, run profile validation |
| `docker` | Docker image build |

## How It Works

The script runs `nektos/act:latest` as a Docker container with:

- `/var/run/docker.sock` mounted — so act can spin up sibling containers for each workflow step
- The repo root mounted at `/workspace` — the code to act on
- `.act/` mounted at `/workspace/.act` — acts as the act cache directory (runner downloads, etc.)

## Notes

- Uses `--pull=false` inside the act container by default to avoid re-downloading the runner image on every run.
- Some steps (Docker Hub push, GHCR attestation, Trivy scan) may fail locally because they depend on registry access or secrets. That's expected — the focus is on pre-CI validation.
- The `lint-and-test` job is the fastest feedback loop for catching formatting and build errors.
