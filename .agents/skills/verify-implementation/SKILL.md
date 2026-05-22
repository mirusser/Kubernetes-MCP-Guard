---
name: verify-implementation
description: Read-only audit of your recent implementation (staged files) against its plan and the repo's standards. Use when an implementation is complete in your working tree and you want a meticulous evidence-based report (completeness, code standards, tests, architecture, docs, behavior) before claiming done. Produces findings and offers a remediation plan; never edits code itself. Not for reviewing someone else's PR.
---

# Verify Implementation

Read-only audit of recent implementation. Produces a findings report and offers a remediation plan. Never edits code.

## Required input

One of:

- An implementation plan (from `planning-and-task-breakdown`)
- An explicit scope description from the user

If neither is available, ask: *"What plan or scope should I verify against?"* Do not proceed without one — verification without scope is just unfocused review.

## Workflow

### 1. Orient

Use `repo-onboarding` for context. Skip if already onboarded this session.

### 2. Establish completeness (before quality)

Read the plan/scope, then collect actual changes:

- `git status` and `git diff <base-branch>...HEAD`
- For each plan item: mark **Done / Missing / Partial** with evidence (`file:line` or "no change").
- For each changed file: mark **Planned / Drift** (touched but not in plan).

A well-coded missing feature is still incomplete. A correctly-implemented unplanned change is still drift.

### 3. Dispatch parallel review agents

Dispatch four Explore subagents in a **single message**. Give each the plan, the diff summary, and the skill name. Each returns findings as `file:line` + quoted snippet + severity (Blocker / Important / Nice-to-have).

| Agent | Lens | Skill |
|---|---|---|
| Standards reviewer | Naming, magic strings, async hygiene, logging, var/records/sealed | `code-standards` |
| Test reviewer | Coverage of new behavior, naming, assertion surface, missing edges, `InternalsVisibleTo` | `writing-tests` |
| Docs reviewer | README drift, `docs/configuration.md` accuracy, env vars, tool contracts | `verify-readme-docs` |
| Architecture reviewer | Module depth, seams, shallow wrappers, ADR conflicts, CONTEXT.md vocabulary | `improve-codebase-architecture` |

Brief each: *"You are read-only. Report findings only. Cite `file:line` and quote the offending snippet. Severity bucket per finding. Do not propose code edits."*

### 4. Codegraph impact pass

For each meaningfully changed symbol (added function/class, removed symbol, signature change), run `codegraph_impact`. Flag callers/dependents that were not updated. This catches the "ripple" bugs unit tests miss.

### 5. Run tests

Per the `run-tests` skill. Default to `./scripts/run-tests.sh`. If the change is narrowly scoped to one project, run that project's test command directly. Capture:

- Which tiers ran
- Which were skipped (and why — no Docker, no K8s, env-var not set)
- Which failed (with the failing test names)

### 6. Behavioral check

Tests passing ≠ feature working. For the primary acceptance criterion, exercise the runtime path (HTTP endpoint, CLI command, MCP tool, manifest apply) and observe the result. If you cannot run it (no infra, missing cluster, no kubeconfig), state that explicitly in the report — never silently skip.

### 7. Compile the report

Use the template below. Every finding cites `file:line` and quotes the snippet.

### 8. Offer remediation

End with: *"Want me to draft a remediation plan using `planning-and-task-breakdown`?"* Only invoke that skill on user confirmation. Do not edit code as part of verification.

## Report template

````markdown
## Verification Report: [feature/scope]

### Completeness
| Plan item | Status | Evidence |
|---|---|---|
| ... | Done / Missing / Partial | `file:line` or "no change" |

Scope drift: [files touched outside plan, or "none"]

### Findings

#### Blockers
- **[short title]** — `src/foo/Bar.cs:42`
  > `quoted snippet`
  Why this blocks: ...

#### Important
- **[short title]** — `src/foo/Baz.cs:17`
  > `quoted snippet`
  ...

#### Nice-to-have
- ...

### Codegraph impact
- `Symbol.Method` — N callers, all updated / 2 unupdated (`src/x.cs:10`, `src/y.cs:55`)

### Tests
- Ran: [tiers + counts]
- Skipped: [tiers + reason]
- Failures: [list or "none"]

### Behavioral check
[What you ran, what you observed — or why you could not run it.]

### Acceptance criteria
- [x] [criterion] — evidence
- [ ] [criterion] — gap evidence

### Recommendation
Ready to merge / Remediation needed / Cannot fully verify — [reason]
````

## Discipline

- **Read-only.** Spotting a one-line typo? Report it; do not fix.
- **No "looks good" without citations.** Either you have evidence or you have a question.
- **Distinguish "no issues found" from "I did not check this."** Be explicit about coverage in the report.
- **Honor existing ADRs.** Following one is a note; contradicting one is a finding.
- **Convert findings to plan tasks only on confirmation.** Verification ends at the report.

## Red flags

| Thought | Reality |
|---|---|
| "I'll fix this small one inline" | Verification ends the moment you edit. Stop. |
| "Tests pass, ship it" | Tests passing ≠ feature working. Check the runtime path. |
| "No plan, I'll figure out scope as I go" | Stop. Ask for plan or scope. |
| "Skipping architecture check, looks fine" | Then say so in the report. Do not pretend you checked. |
| "codegraph_impact is overkill" | A two-call pass catches missed callers cheaply. Do it. |
| "I'll just draft the remediation plan too" | Only on user confirmation. Keep verify and fix separate. |
