---
name: sonar-local-remediaton
description: Consume the saved local SonarQube report at `.sonarqube-local/reports/sonarqube-local-report.json` and produce a structured remediation plan for findings in k8s-toolkit. Use after running `$run-local-sonarqube` or `tools/sonarqube/run-analysis.sh` when Codex needs to triage local SonarQube issues, quality gate, coverage, measures, or hotspots from the on-disk report. Chains to repo-onboarding, code-standards, planning-and-task-breakdown, writing-tests, and verify-readme-docs.
---

# Sonar Local Remediation

Use this skill to turn the local SonarQube Community Build report into an ordered, verifiable remediation plan that respects this repository's conventions.

## Prerequisites

The local report must exist at:

```text
.sonarqube-local/reports/sonarqube-local-report.json
```

If the report is missing or stale, first use `.agents/skills/run-local-sonarqube/SKILL.md` or run the local tooling:

```bash
docker compose -f tools/sonarqube/docker-compose.yml up -d
tools/sonarqube/prepare-local.sh
tools/sonarqube/run-analysis.sh
```

Do not use `.sonarqube/` as the durable report source. The .NET scanner recreates that directory during analysis. The agent-ingestible report is saved under `.sonarqube-local/reports/`.

## Workflow

### Step 1 - Orient in the repository

Read `.agents/lessons.md`, then load `.agents/skills/repo-onboarding/SKILL.md` to read `AGENTS.md`, `README.md`, and the project READMEs relevant to the affected files. This establishes the structural and architectural context needed to propose changes that fit the codebase.

### Step 2 - Load code conventions

Load `.agents/skills/code-standards/SKILL.md`. Every fix must follow the repo's C# conventions: lower-camel-case private fields, file-scoped namespaces, `sealed` by default, `ConfigureAwait(false)` on awaited tasks in library code, structured logging via `ILogger<T>`, and the rest of the norms listed there.

### Step 3 - Parse and triage the local report

Read `.sonarqube-local/reports/sonarqube-local-report.json` and group findings:

- **Type**: `BUG`, `VULNERABILITY`, `CODE_SMELL`, or other SonarQube issue types present in the report.
- **Severity**: `BLOCKER`, `CRITICAL`, `MAJOR`, `MINOR`, `INFO`.
- **Project/file**: group by `component`.

Priority order for remediation:

1. BLOCKER and CRITICAL bugs and vulnerabilities.
2. Failed quality gate conditions from `qualityGate.projectStatus`.
3. MAJOR code smells.
4. MINOR and INFO findings.
5. Hotspots that are not already reviewed or accepted.

For each issue, note:

- `component` - affected file path in SonarQube's component format.
- `line` or `textRange` - issue location.
- `message` - finding description.
- `rule` - SonarQube rule ID.
- `severity`, `type`, and `impacts` - remediation priority signals.
- `effort` or `debt` - estimated fix time.

For `hotspots[]`, summarize security-review work separately from normal issues. Do not change code for a hotspot that is already reviewed as safe unless the user explicitly asks.

Use `measures.component.measures[]` to call out quality-gate context such as coverage, duplication, reliability, security, maintainability, complexity, and cognitive complexity.

### Step 4 - Understand context before proposing fixes

For each priority group, read the affected source files before proposing a fix. Understand the surrounding code, the type's responsibility, and whether a fix is local or affects callers. Do not propose a code change from the Sonar message alone.

### Step 5 - Build the remediation plan

Load `.agents/skills/planning-and-task-breakdown/SKILL.md` and use its task format. Group related findings into one task, for example "Fix cancellation handling in McpGateway" or "Document intentional K8s naming exceptions". Do not create one task per issue when findings share the same root cause.

Each task must include:

- A clear title and description.
- Acceptance criteria.
- Verification steps such as build, test, rerun local Sonar analysis, or inspect the saved report.
- File paths and lines affected.
- Dependency on prior tasks if any.

Order tasks so foundational fixes come before dependent fixes.

### Step 6 - Test additions

If a fix requires new or modified tests, load `.agents/skills/writing-tests/SKILL.md`. Follow its conventions: one test class per production class named `{TypeUnderTest}Tests`, method names as `Method_State_ExpectedResult`, no shared mutable state, `[Theory]` with `[InlineData]` over repeated `[Fact]` tests, and `InternalsVisibleTo` when testing internal types.

### Step 7 - Documentation check

After implementing fixes, load `.agents/skills/verify-readme-docs/SKILL.md` and check whether public API surface, configuration, environment variables, MCP tool contracts, or local Sonar instructions require documentation updates.

## Report JSON Schema Reference

```text
.sonarqube-local/reports/sonarqube-local-report.json
├── metadata
│   ├── generatedAt
│   ├── projectKey
│   ├── sonarUrl
│   ├── dashboardUrl
│   └── source              "local-sonarqube"
├── qualityGate             /api/qualitygates/project_status response
│   └── projectStatus.status  "OK" | "ERROR" | "WARN"
├── measures                /api/measures/component response
│   └── component.measures[]  [{metric, value, bestValue}]
├── issues[]                /api/issues/search results
│   ├── key, rule, severity, type
│   ├── component, project, line, textRange
│   ├── message, status, issueStatus
│   ├── effort / debt
│   ├── impacts[]
│   └── flows[]
└── hotspots[]              /api/hotspots/search results
    ├── key, component, line, message
    ├── securityCategory
    ├── vulnerabilityProbability
    └── status
```

## Guardrails

- Do not fix findings that are not in the local report unless the user explicitly broadens scope.
- Do not introduce broad analyzer suppressions or `#pragma warning disable` as a fix. Resolve the issue or document a focused false positive.
- Do not change unrelated code while fixing findings. Keep edits surgical.
- Do not introduce new public API without confirming it is needed by the fix.
- Do not rename `K8s` symbols to `K8S` for Sonar S101. `K8s` is the repository convention; keep or add a local `// Justification:` comment.
- If a finding is a false positive, document it with a focused `// Justification:` comment and flag it in the plan for human review.
- Local analysis may report missing blame for uncommitted files. Note it in the plan only if it affects triage; do not rewrite unrelated files to satisfy blame metadata.
