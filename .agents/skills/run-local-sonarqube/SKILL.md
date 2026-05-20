---
name: run-local-sonarqube
description: Run the repository-local SonarQube Community Build scan for k8s-toolkit and save an agent-ingestible JSON report on disk. Use when Codex needs local SonarQube/Sonar scanner setup, pre-push Sonar analysis, Sonar findings export, local quality gate/coverage/issue reporting, or validation that `.sonarqube-local/reports/sonarqube-local-report.json` exists after analysis.
---

# Run Local SonarQube

## Purpose

Use the repo-owned SonarQube tooling in `tools/sonarqube/` to run the same .NET scanner flow as CI against a local SonarQube server and persist a report for later agents.

The durable report path is:

```text
.sonarqube-local/reports/sonarqube-local-report.json
```

Never use `.sonarqube/` for durable credentials or reports. The .NET scanner owns `.sonarqube/` as scratch state and recreates it during analysis.

## Workflow

1. Read `tools/sonarqube/README.md` if the scripts or expected paths are unfamiliar.
2. Validate the host has Docker Compose v2, `curl`, `jq`, `openssl`, and .NET 10.
3. Start the local stack if needed:

```bash
docker compose -f tools/sonarqube/docker-compose.yml up -d
```

4. Initialize or refresh local SonarQube state:

```bash
tools/sonarqube/prepare-local.sh
```

This waits for SonarQube, changes the default admin password on fresh volumes, creates the default project, generates or validates a token, and writes ignored credentials to `.sonarqube-local/local.env`.

5. Run the scan:

```bash
tools/sonarqube/run-analysis.sh
```

This restores local tools, starts `dotnet-sonarscanner`, restores/builds `InfraGate.slnx`, runs default non-Keycloak tests with OpenCover output, ends the scan, waits for the SonarQube Compute Engine task, and exports the JSON report.

6. Prove the report exists and is useful before claiming success:

```bash
jq '{project: .metadata.projectKey, source: .metadata.source, qualityGate: (.qualityGate.projectStatus.status // null), measures: (.measures.component.measures | length), issues: (.issues | length), hotspots: (.hotspots | length)}' \
  .sonarqube-local/reports/sonarqube-local-report.json
```

Report success only if that command reads the report and shows a non-null project/source plus measure/issue/hotspot counts.

## Sandbox And Approval Notes

In this repo's Codex sandbox, Docker commands can start the SonarQube stack, but HTTP calls to `http://localhost:9000` from `curl` or the .NET scanner may require elevated execution. If `curl` cannot connect to `localhost:9000` from the sandbox while `docker compose ps` shows the service running, rerun `prepare-local.sh`, `run-analysis.sh`, or direct SonarQube API probes with approval.

Use a mixed-case/digit/special generated password for default-admin reset. SonarQube rejected pure hex passwords during setup.

## Expected Output

The report has this top-level shape, matching the local SonarQube export contract:

```json
{
  "metadata": {},
  "qualityGate": {},
  "measures": {},
  "issues": [],
  "hotspots": []
}
```

The default local project key is `mirusser_Kubernetes-MCP-Guard`. Override with `SONAR_PROJECT_KEY` only when the user explicitly asks for a different local project.

## Failure Handling

- If `.sonarqube-local/local.env` is missing, rerun `tools/sonarqube/prepare-local.sh`.
- If the report is missing after `run-analysis.sh`, do not claim success; inspect scanner output and rerun after fixing the cause.
- If measures export fails, check local supported metric keys via `/api/metrics/search`; Community Build metric names can differ from SonarCloud.
- If analysis warns about missing blame, note dirty or untracked files rather than editing unrelated worktree changes.
