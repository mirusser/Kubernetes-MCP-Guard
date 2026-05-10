---
name: sonarcloud-remediation
description: Consume a downloaded sonarcloud-report.json artifact and produce a structured remediation plan for findings in k8s-toolkit. Use after downloading the artifact from a GitHub Actions run. Chains to code-standards, writing-tests, planning-and-task-breakdown, repo-onboarding, and verify-readme-docs.
---

# SonarCloud Remediation

Use this skill to turn a downloaded `sonarcloud-report.json` artifact into an ordered, verifiable remediation plan that respects this repository's conventions.

## Prerequisites

The `sonarcloud-report.json` file must be downloaded manually from the GitHub Actions run page:
1. Open the `SonarCloud Analysis` workflow run on the repository's Actions page.
2. Scroll to the **Artifacts** section and download `sonarcloud-report`.
3. Extract the JSON file and provide it as context (drag into the conversation or paste the path).

The report is generated only on `push` and `workflow_dispatch` events — PR runs do not produce it.

## Workflow

### Step 1 — Orient in the repository

Load `.agents/skills/repo-onboarding/SKILL.md` to read `AGENTS.md`, `README.md`, and the project READMEs relevant to the affected files. This establishes the structural and architectural context needed to propose changes that fit the codebase rather than fight it.

### Step 2 — Load code conventions

Load `.agents/skills/code-standards/SKILL.md`. Every fix must follow the repo's C# conventions: lower-camel-case private fields, file-scoped namespaces, `sealed` by default, `ConfigureAwait(false)` on awaited tasks in library code, structured logging via `ILogger<T>`, and the rest of the norms listed there. Do not propose fixes that violate these standards.

### Step 3 — Parse and triage the report

Read `sonarcloud-report.json` and group findings:

- **Type**: `BUG`, `VULNERABILITY`, `CODE_SMELL`
- **Severity**: `BLOCKER`, `CRITICAL`, `MAJOR`, `MINOR`, `INFO`
- **Project/file**: group by `component` (file path)

Priority order for remediation:
1. BLOCKER and CRITICAL bugs and vulnerabilities
2. MAJOR code smells
3. MINOR and INFO findings

For each finding, note:
- `component` — affected file path
- `line` — line number in that file
- `message` — the finding description
- `rule` — rule ID and `ruleDescription.descriptionSections` for the full remediation context
- `effort` — estimated fix time

### Step 4 — Understand context before proposing fixes

For each priority group, read the affected source files. Understand the surrounding code, the type's responsibility, and whether a fix is local or ripples through callers. Do not propose a fix without reading the file first.

### Step 5 — Build the remediation plan

Load `.agents/skills/planning-and-task-breakdown/SKILL.md` and use its task format. Group related findings into one task (e.g., "Fix nullability warnings in McpGateway", "Add ConfigureAwait to all async I/O in InfraGate.McpServer"). Do not create one task per issue — group by logical cluster.

Each task must include:
- A clear title and description
- Acceptance criteria (what done looks like)
- Verification steps (build, test, analyzer)
- File paths and line ranges affected
- Dependency on prior tasks if any

Order tasks so foundational fixes (e.g., removing a bad base class) come before dependent fixes (e.g., callers of that class).

### Step 6 — Test additions

If a fix requires new or modified tests, load `.agents/skills/writing-tests/SKILL.md`. Follow its conventions: one test class per production class named `{TypeUnderTest}Tests`, method names as `Method_State_ExpectedResult`, no shared mutable state, `[Theory]` with `[InlineData]` over repeated `[Fact]` tests, and `InternalsVisibleTo` when testing internal types.

### Step 7 — Documentation check

After implementing fixes, load `.agents/skills/verify-readme-docs/SKILL.md` and check whether any changes to public API surface, configuration, environment variables, or MCP tool contracts require updates to `docs/configuration.md` or project READMEs.

## Report JSON Schema Reference

```
sonarcloud-report.json
├── metadata
│   ├── generatedAt      ISO-8601 timestamp
│   ├── projectKey       SonarCloud project key
│   ├── sonarcloudUrl    Link to the project dashboard
│   └── branch           Git branch that triggered the scan
├── qualityGate          Full /api/qualitygates/project_status response
│   └── projectStatus.status  "OK" | "ERROR" | "WARN"
├── measures             Full /api/measures/component response
│   └── component.measures[]  [{metric, value, bestValue}]
├── issues[]             /api/issues/search results with additionalFields=rules
│   ├── key, rule, severity, type (BUG/VULNERABILITY/CODE_SMELL)
│   ├── component        File path (e.g. "project:src/Foo/Bar.cs")
│   ├── line             Line number
│   ├── message          Finding description
│   ├── effort / debt    Fix time estimate
│   └── ruleDescription
│       └── descriptionSections[]  [{key, content}] — root/compliant/remediation blocks
└── hotspots[]           /api/hotspots/search results
    ├── key, component, line, message
    ├── securityCategory
    ├── vulnerabilityProbability
    └── status
```

## Guardrails

- Do not fix what is not in the report. Scope is the findings list.
- Do not introduce broad analyzer suppressions or `#pragma warning disable` as a fix — resolve the actual issue.
- Do not change unrelated code while fixing findings. Keep edits surgical.
- Do not introduce new public API without confirming it is needed by the fix.
- If a finding is a false positive (SonarCloud rule does not apply to this context), document it in a `// Justification:` comment rather than suppressing silently, and flag it in the plan for human review.
