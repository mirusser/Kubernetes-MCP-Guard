# Implementation Plan: SonarCloud Report Export

## Overview

Add a CI step to `sonar.yml` that fetches the full SonarCloud analysis results (issues, quality gate, measures, hotspots) via the SonarCloud Web API, assembles them into a structured JSON file, and uploads it as a downloadable GitHub Actions workflow artifact. The report is designed for AI agent consumption: each issue includes file path, line number, severity, message, effort estimate, rule ID, and the full rule description with remediation sections.

Additionally, create a repo-local agent skill (`sonarcloud-remediation`) that teaches AI agents how to consume the downloaded report and produce a structured remediation plan using the repo's existing skills (code-standards, writing-tests, planning-and-task-breakdown, etc.).

No new secrets, variables, tools, or dependencies are required. The step reuses `SONAR_TOKEN` and `SONAR_PROJECT_KEY` already available in the workflow.

## Architecture Decisions

- **Format: JSON**. A single `sonarcloud-report.json` with four top-level keys (`issues`, `qualityGate`, `measures`, `hotspots`) plus a `metadata` section with timestamp and project key. JSON is structured, universally parseable by AI agents, and maps directly to the API responses.
- **No Markdown summary generated in the workflow**. JSON is the primary deliverable. An AI consumer can synthesize its own summary.
- **Pagination handled**. The issues endpoint returns up to 500 results per page. The export script loops through all pages.
- **`additionalFields=rules`** on the issues endpoint includes rule descriptions and remediation sections — the most valuable data for an AI agent that needs to understand and fix findings.
- **Placement: after "End Sonar scan"** in the existing `sonar.yml` workflow. By this point the analysis is fully uploaded and the API reflects the latest state.
- **No new script file**. The export logic lives inline in the workflow YAML as a `run:` block (bash + curl + jq). This avoids a separate maintenance surface for a ~50-line integration script.

## Task List

### Phase 1: Export report from SonarCloud API

#### Task 1: Add report fetch and upload step to sonar.yml

**Description:** Insert a new YAML step after "End Sonar scan" that calls four SonarCloud Web API endpoints, merges the responses into a single JSON object, writes `sonarcloud-report.json`, and uploads it as a workflow artifact.

**Acceptance criteria:**
- [ ] Step uses `if: github.event_name != 'pull_request'` to skip PR analysis noise (PRs already show results in the PR decoration)
- [ ] `curl` calls authenticate with `SONAR_TOKEN` via HTTP basic auth (`$SONAR_TOKEN:` as the password, empty username)
- [ ] Issues are fetched with `additionalFields=rules` and paginated to exhaust all pages
- [ ] Quality gate, measures (all key metrics), and hotspots are fetched
- [ ] Output is written to a single `sonarcloud-report.json` file
- [ ] File is uploaded via `actions/upload-artifact@v4` with retention set (e.g., 7 days)
- [ ] API failures (non-2xx) print the response body to stderr but do not fail the entire workflow (`|| true` with a warning)

**Verification:**
- [ ] Push to `dev` triggers `sonar.yml` and produces a downloadable artifact
- [ ] Downloaded artifact contains valid JSON with all four top-level keys (`metadata`, `qualityGate`, `measures`, `issues`, `hotspots`)
- [ ] Each issue entry includes `rule` sub-object with `descriptionSections` (the full rule description)

**Dependencies:** None (relies on existing `SONAR_TOKEN`, `SONAR_PROJECT_KEY`, and the preceding Sonar scan)

**Files likely touched:**
- `.github/workflows/sonar.yml`

**Estimated scope:** Small (1 file, ~20 lines of YAML)

---

### Phase 2: Create the sonarcloud-remediation agent skill

#### Task 2: Write the sonarcloud-remediation skill definition

**Description:** Create `.agents/skills/sonarcloud-remediation/SKILL.md` — a repo-local skill that instructs AI agents how to consume a downloaded `sonarcloud-report.json` and produce a structured remediation plan, using the repo's existing skills for guidance on code conventions, test patterns, and documentation norms.

**Skill workflow (the instructions the agent follows):**

1. Load `repo-onboarding` skill to orient in the repository structure and discover relevant README docs for the affected projects.
2. Load `code-standards` skill — every fix must respect the repo's C# conventions (field naming, var usage, sealed classes, file-scoped namespaces, structured logging, etc.).
3. Parse the `sonarcloud-report.json` file:
   - Group issues by type (BUG, VULNERABILITY, CODE_SMELL), severity (BLOCKER, CRITICAL, MAJOR, MINOR, INFO), and project/file.
   - Prioritize: BLOCKER/CRITICAL bugs and vulnerabilities first, then MAJOR code smells, then MINOR/INFO.
4. For each issue, read the affected source file(s) to understand the surrounding code before proposing a fix.
5. Generate a remediation plan using the `planning-and-task-breakdown` skill template:
   - One task per logical group of related fixes (e.g., "Fix nullability warnings in InfraGate.McpGateway", "Add missing ConfigureAwait calls", "Remove unused imports").
   - Each task has acceptance criteria, verification steps, and file paths.
   - Tasks ordered by dependency (foundational fixes first, dependent fixes later).
6. If the plan involves test changes, load `writing-tests` skill to ensure test additions follow the repo's conventions (`Method_State_ExpectedResult`, project structure, InternalsVisibleTo).
7. After implementing fixes, load `verify-readme-docs` skill to check if any documentation (configuration.md, project READMEs) needs updating due to the changes.

**Acceptance criteria:**
- [ ] Skill file exists at `.agents/skills/sonarcloud-remediation/SKILL.md`
- [ ] Skill description in YAML frontmatter describes when to load it
- [ ] Skill references the required existing skills: `repo-onboarding`, `code-standards`, `planning-and-task-breakdown`, `writing-tests`, `verify-readme-docs`
- [ ] Skill defines a clear workflow: parse report, group/prioritize, understand context, plan tasks, fix, verify
- [ ] Skill notes that the `sonarcloud-report.json` artifact must be downloaded manually from the GitHub Actions run page and provided as context

**Verification:**
- [ ] Skill file passes YAML frontmatter validation
- [ ] Cross-reference: skill lists the correct paths to the five source skills
- [ ] Manual test: load the skill and give an AI a sample report — confirm it produces a structured plan

**Dependencies:** Task 1 (needs to know the final JSON schema to write accurate instructions)

**Files likely touched:**
- `.agents/skills/sonarcloud-remediation/SKILL.md` (new)

**Estimated scope:** Small (1 new file, ~80-100 lines)

---

### Phase 3: Update documentation

#### Task 3: Document the report artifact and remediation skill

**Description:** Update `docs/configuration.md` to document the sonarcloud-report artifact and the remediation skill. Add a reference in `docs/devs-readme.md` if appropriate.

**Acceptance criteria:**
- [ ] `docs/configuration.md` CI table (or a note after it) documents the sonarcloud-report artifact (JSON format, contents, how to access)
- [ ] Notes that the remediation skill exists at `.agents/skills/sonarcloud-remediation/SKILL.md` for AI-driven fix generation
- [ ] `git diff --check` passes

**Verification:**
- [ ] Doc entries match the actual artifact output keys and skill path
- [ ] Links between docs and skill are correct

**Dependencies:** Task 1, Task 2

**Files likely touched:**
- `docs/configuration.md`
- `docs/devs-readme.md` (possibly)

**Estimated scope:** XS (1 file, ~10 lines)

---

### Phase 4: Verify end-to-end

#### Task 4: End-to-end verification

**Description:** Push to `dev`, download the artifact, validate its contents against the SonarCloud dashboard, and test the remediation skill with the downloaded report.

**Acceptance criteria:**
- [ ] Artifact is downloadable from the GitHub Actions run page
- [ ] Issue count in the JSON matches the SonarCloud dashboard
- [ ] Quality gate status matches the dashboard
- [ ] Measures values match the dashboard
- [ ] The remediation skill produces a valid, well-structured remediation plan when given the report
- [ ] A sample AI prompt using the skill (with a few issues) produces sensible fix suggestions that respect code-standards conventions

**Verification:**
- [ ] Manual: compare JSON counts and values to sonarcloud.io project dashboard
- [ ] Manual: feed a subset of issues through the remediation skill workflow and confirm the output

**Dependencies:** Task 1, Task 2

**Files likely touched:**
- None (verification only)

**Estimated scope:** XS (no code changes)

---

## Implementation Details

### SonarCloud API endpoints

| Endpoint | Method | Auth | Key parameters |
|---|---|---|---|
| `https://sonarcloud.io/api/issues/search` | GET | Basic: `$TOKEN:` | `componentKeys`, `ps=500`, `p=<page>`, `statuses=OPEN,CONFIRMED`, `additionalFields=rules` |
| `https://sonarcloud.io/api/qualitygates/project_status` | GET | Basic: `$TOKEN:` | `projectKey` |
| `https://sonarcloud.io/api/measures/component` | GET | Basic: `$TOKEN:` | `component`, `metricKeys=bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,sqale_rating,reliability_rating,security_rating,security_review_rating,maintainability_rating,ncloc,complexity,cognitive_complexity` |
| `https://sonarcloud.io/api/hotspots/search` | GET | Basic: `$TOKEN:` | `projectKey`, `ps=500`, `p=<page>` |

### Report JSON schema

The output file `sonarcloud-report.json` has this structure:

- **`metadata`**: `generatedAt` (ISO-8601), `projectKey`, `sonarcloudUrl`, `branch` (if available)
- **`qualityGate`**: Full `/api/qualitygates/project_status` response — includes `projectStatus.status`, per-condition results, and period info
- **`measures`**: Full `/api/measures/component` response — includes `component.measures` array with each metric's `metric`, `value`, and `bestValue`
- **`issues`**: Array of issue objects from `/api/issues/search`. Each includes `key`, `rule`, `severity`, `component` (file path), `line`, `textRange`, `message`, `type` (BUG/VULNERABILITY/CODE_SMELL), `effort`, `debt`, `status`, and `ruleDescription` — the full rule definition with name, severity, type, and `descriptionSections` (arrays of {key, content} blocks with root/compliant/remediation context)
- **`hotspots`**: Array of hotspot objects from `/api/hotspots/search`. Each includes `key`, `component`, `line`, `message`, `securityCategory`, `vulnerabilityProbability`, `status`

### Step YAML sketch (inline in sonar.yml)

The new step goes after the existing "End Sonar scan" step. Rough outline:

1. Paginate issues: loop `p=1..N` calling `/api/issues/search` with `additionalFields=rules`, collect all results
2. Fetch quality gate: single GET to `/api/qualitygates/project_status`
3. Fetch measures: single GET to `/api/measures/component` with all relevant metric keys
4. Fetch hotspots: paginated GET to `/api/hotspots/search`
5. Assemble with `jq -n` into the target JSON schema
6. Upload with `actions/upload-artifact@v4`

### Skill file sketch

The remediation skill follows the existing skill format (see `.agents/skills/code-standards/SKILL.md` for structure):

- YAML frontmatter with `name: sonarcloud-remediation` and a `description` field
- Workflow section with ordered steps
- References to the five prerequisite skills with exact file paths
- Notes on how to receive the report (manual download from workflow artifacts)

## Dependency Graph

```
sonar.yml (existing Sonar scan)
    |
    +-- Task 1: Export step (fetches JSON artifact)
            |
            +-- Task 2: Remediation skill (consumes the JSON)
            |       |
            |       +-- Task 4: End-to-end verification
            |
            +-- Task 3: Documentation update
                    |
                    +-- (depends on both Task 1 and Task 2)
```

Tasks 2 and 3 can be parallelized after Task 1 is complete.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| SonarCloud API returns 401/403 (expired or misconfigured token) | Low — workflow step warns but doesn't fail the job | `|| true` fallback with stderr message; CI still green |
| Large project with thousands of issues exceeds API rate limits | Low — SonarCloud allows generous page sizes | Use `ps=500` per page; a few hundred issues = 1-2 calls |
| `jq` not available on runner | Low — `ubuntu-latest` includes `jq` | Not a concern for this workflow since sonar.yml runs on `ubuntu-latest` (not self-hosted) |
| Report JSON too large for artifact upload | Low — even 1000 issues x 2KB each = ~2MB | Monitor artifact size; consider gzip if needed |
| Remediation skill produces plans that don't align with repo conventions | Medium | Skill explicitly chains to `code-standards` and `writing-tests`; verification step includes manual review |

## Open Questions
- Should the export step run on PR events too, or only push/dispatch? (Current plan: push + dispatch only, as PRs already show results in the PR decoration.)
- Should the JSON be gzipped to reduce artifact size? (Current plan: uncompressed; switch to gzip if artifacts exceed ~10MB.)
- Should there be a follow-up skill that automates applying the remediation plan (i.e., actually fixing the code)? (Current plan: out of scope for this plan; the remediation skill produces the plan, human or AI applies it separately.)
