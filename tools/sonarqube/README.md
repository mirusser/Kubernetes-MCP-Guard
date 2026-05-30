# Local SonarQube

This directory contains the local SonarQube Community Build setup for InfraGate. It mirrors the GitHub Actions Sonar flow, but points the scanner at a local SonarQube server instead of SonarCloud.

Prerequisites: Docker Compose v2, `curl`, `jq`, `openssl`, and the .NET 10 SDK.

## Start SonarQube

```bash
docker compose -f tools/sonarqube/docker-compose.yml up -d
docker compose -f tools/sonarqube/docker-compose.yml logs -f sonarqube
```

Open `http://localhost:9000`. The first login is `admin` / `admin`; SonarQube will require a password change.

The Compose file uses local-only port binding, SonarQube Community Build, PostgreSQL 17, and named Docker volumes for server and database state.

Agent note: in the Codex sandbox used for this repo, Docker commands can start the stack, but HTTP calls to `http://localhost:9000` from `curl` or the .NET scanner require elevated execution. Future agents should run `prepare-local.sh` and `run-analysis.sh` with approval when local networking is sandboxed.

## Create Project and Token

Let the setup script initialize the local server:

```bash
./tools/sonarqube/prepare-local.sh
```

On a fresh SonarQube volume this script changes the default `admin` password, creates the local project, generates a user token, and writes the ignored file `.sonarqube-local/local.env`. The analysis script loads that file automatically.

Do not store persistent credentials or reports under `.sonarqube/`; the .NET scanner uses that path as scratch state and recreates it during analysis. Use `.sonarqube-local/` for agent state.

The default local project key is:

```text
mirusser_Kubernetes-MCP-Guard
```

To use a manually generated token instead, export it before running the scan:

```bash
export SONAR_TOKEN="paste-token-here"
```

Do not commit tokens or shell history files containing tokens. `.sonarqube-local/` is ignored by git.

## Run Analysis

From the repository root:

```bash
./tools/sonarqube/run-analysis.sh
```

The script resolves the repository root from its own path, so absolute or relative invocation from another directory also works.

Optional overrides:

```bash
SONAR_PROJECT_KEY="local-infragate" \
SONAR_PROJECT_NAME="InfraGate Local" \
SONAR_HOST_URL="http://localhost:9000" \
tools/sonarqube/run-analysis.sh
```

The script runs the same core scanner flow as `.github/workflows/sonar.yml`:

```text
dotnet tool restore
dotnet-sonarscanner begin
dotnet restore InfraGate.slnx
dotnet build InfraGate.slnx --configuration Release --no-restore
dotnet test InfraGate.slnx --configuration Release --no-build --filter "Category!=Keycloak" with OpenCover output
dotnet-sonarscanner end
```

The local scan intentionally omits SonarCloud-only organization settings and uses `sonar.host.url=http://localhost:9000`.

After upload and Compute Engine processing, the script writes an agent-ingestible report:

```text
.sonarqube-local/reports/sonarqube-local-report.json
```

Override the path with `SONAR_REPORT_PATH` if a different ignored location is needed.

## Stop or Reset

Stop the server while keeping analysis history:

```bash
docker compose -f tools/sonarqube/docker-compose.yml down
```

Delete the local SonarQube database and start fresh:

```bash
docker compose -f tools/sonarqube/docker-compose.yml down -v
```

## Linux Host Limits

If the `sonarqube` container fails with Elasticsearch bootstrap or file-limit errors, check the host limits:

```bash
sysctl vm.max_map_count
sysctl fs.file-max
ulimit -n
ulimit -u
```

Set the kernel values for the current boot:

```bash
sudo sysctl -w vm.max_map_count=524288
sudo sysctl -w fs.file-max=131072
```

For a persistent local setup:

```bash
printf "vm.max_map_count=524288\nfs.file-max=131072\n" | sudo tee /etc/sysctl.d/99-sonarqube.conf
sudo sysctl --system
```

SonarQube Community Build is useful for local pre-push checks, but it is not a full SonarCloud replacement. Keep SonarCloud as the CI source of truth.

References:

- [SonarQube Community Build Docker setup](https://docs.sonarsource.com/sonarqube-community-build/server-installation/from-docker-image/set-up-and-start-container)
- [SonarQube Linux host requirements](https://docs.sonarsource.com/sonarqube-community-build/server-installation/pre-installation/linux)
- [SonarScanner for .NET usage](https://docs.sonarsource.com/sonarqube-server/2026.1/analyzing-source-code/scanners/dotnet/using)
