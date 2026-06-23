# Extraction Plan: InfraGate.RfcRag → Standalone Repository

**Date:** 2026-06-08
**Author:** Sisyphus (AI agent)
**Status:** Draft — awaiting review

---

## Overview

Extract InfraGate.RfcRag from the k8s-toolkit monorepo (`InfraGate.slnx`) into its own solution file (`.slnx`) and repository. The project is fully standalone — zero internal `ProjectReference` dependencies — which makes extraction a largely mechanical exercise of copying files and adapting paths.

## Facts Confirmed

| Aspect | Finding |
|--------|---------|
| Internal deps | **None** — only NuGet packages (Dapper, MEAI, Npgsql, Pgvector, MCP) |
| Project type | `Exe` — stdio MCP server |
| Test project | `InfraGate.RfcRag.Tests` — xUnit v3 + Testcontainers (39 tests: 33 unit, 6 integration) |
| Docker support | Dockerfile + docker-compose + env example — all exist |
| Shared build config | `Directory.Build.props` at repo root (TFM `net10.0`, analyzers, `TreatWarningsAsErrors`) |
| CI references | **None** — no workflow touches RfcRag |
| Solution membership | `InfraGate.slnx` only |
| Migrations | 4 SQL files under `src/InfraGate.RfcRag/Migrations/` (embedded as content) |
| External consumers | **None** — no other project references it |

### What InfraGate.RfcRag is

A standalone stdio MCP server that provides AI agents with RFC search capabilities:
- Indexes ~9,800 RFCs from a local mirror into PostgreSQL with pgvector
- Hybrid search: vector similarity + full-text lexical search with reciprocal rank fusion
- 10 MCP tools: search_rfc, get_rfc, get_rfc_section, get_rfc_toc, search_normative, search_abnf, find_updates_obsoletes, rfc_stats, get_rfc_metadata, list_indexed_rfcs
- Uses OpenRouter API for embedding generation (text-embedding-3-small)
- SHA256-based incremental indexing (subsequent starts complete in seconds)

### Directory inventory (what needs to move)

```
src/InfraGate.RfcRag/
├── Indexing/
│   ├── EmbeddingService.cs
│   ├── IIndexerService.cs
│   ├── IndexingRepository.cs
│   └── RfcIndexer.cs
├── Infrastructure/
│   ├── MissingApiKeyEmbeddingGenerator.cs
│   ├── OpenAiEmbeddingGeneratorAdapter.cs
│   ├── RfcRagConventions.cs
│   ├── RfcRagMigrationRunner.cs
│   ├── RfcRagStartupService.cs
│   └── ServiceCollectionExtensions.cs
├── Migrations/
│   ├── 0001-initial-rfc-rag-schema.sql
│   ├── 0002-add-metadata-fields.sql
│   ├── 0003-add-extended-metadata-fields.sql
│   └── 0004-add-grammar-style.sql
├── Models/
│   ├── GrammarStyleConstants.cs
│   ├── NormativeOccurrence.cs
│   ├── RfcAbnfBlock.cs
│   ├── RfcMetadata.cs
│   └── RfcSection.cs
├── Parsing/
│   ├── RfcDocument.cs
│   └── RfcParser.cs
├── Search/
│   ├── ISearchService.cs
│   ├── MetadataRepository.cs
│   ├── SearchRepository.cs
│   ├── SearchResult.cs
│   └── SearchService.cs
├── Settings/
│   └── RfcRagOptions.cs
├── Tools/
│   ├── RfcRagTools.cs
│   └── ToolExceptionFilter.cs
├── Dockerfile
├── GlobalUsings.cs
├── InfraGate.RfcRag.csproj
├── Program.cs
└── README.md

tests/InfraGate.RfcRag.Tests/
├── Fakes/
│   ├── FakeEmbeddingGenerator.cs
│   ├── FakeSearchService.cs
│   ├── SemanticFakeEmbeddingGenerator.cs
│   └── TrackingEmbeddingGenerator.cs
├── IntegrationTests/
│   ├── EmbeddingIntegrationTests.cs
│   ├── LiveApiIndexingTests.cs
│   └── RfcRagIntegrationTests.cs
├── UnitTests/
│   ├── EmbeddingServiceTests.cs
│   ├── RfcParserTests.cs
│   ├── RfcRagOptionsTests.cs
│   ├── RfcRagToolsTests.cs
│   ├── SearchRepositoryTests.cs
│   └── ToolExceptionFilterTests.cs
├── TestData/
│   ├── rfc2119.txt
│   ├── rfc3986.txt
│   ├── rfc8446.txt
│   ├── rfc9000.txt
│   ├── rfc9110.txt
│   ├── rfc9999.txt
│   └── badfile.txt
├── InfraGate.RfcRag.Tests.csproj
└── README.md

deploy/compose/
├── rfc-rag.yaml
└── rfc-rag.env.example
```

### NuGet dependencies (both projects)

**InfraGate.RfcRag.csproj:**
- Dapper 2.1.79
- Microsoft.Extensions.AI 10.6.0
- Microsoft.Extensions.AI.OpenAI 10.6.0
- Microsoft.Extensions.Hosting 10.0.8
- Microsoft.Extensions.Logging.Abstractions 10.0.8
- Microsoft.Extensions.Options 10.0.8
- ModelContextProtocol 1.3.0
- Npgsql 10.0.2
- Pgvector 0.3.0

**InfraGate.RfcRag.Tests.csproj:**
- coverlet.collector 10.0.1
- Microsoft.NET.Test.Sdk 18.5.1
- Testcontainers.PostgreSql 4.12.0
- xunit.v3 3.2.2
- xunit.runner.visualstudio 3.1.5
- ProjectReference: InfraGate.RfcRag

---

## Architecture Decisions

- **New repo name**: `rfc-rag` (matches docker-compose project name and existing usage in docs; drops InfraGate prefix since it's no longer part of that ecosystem)
- **New solution**: `RfcRag.slnx` at repo root
- **Build config**: Inline the shared `Directory.Build.props` settings into a local one (don't inherit from monorepo)
- **CI/CD**: Fresh GitHub Actions workflow — build + test + Docker publish (GHCR)
- **Repo docs**: Minimal set — README.md, LICENSE, CHANGELOG.md, SECURITY.md, CONTRIBUTING.md

---

## Task List

### Phase 1: New Repository Scaffold

#### Task 1: Create `.slnx` solution file and project layout

**Description:** Create the new solution structure at the extraction target with the RfcRag project and test project, mirroring the existing layout but flattened by one directory level.

**Acceptance criteria:**
- [ ] `RfcRag.slnx` at repo root with two projects: `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` and `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj`
- [ ] Solution builds with `dotnet build RfcRag.slnx`

**Verification:**
- [ ] `dotnet build RfcRag.slnx` exits 0
- [ ] `dotnet test RfcRag.slnx --filter "Category!=Integration"` exits 0

**Dependencies:** None

**Files likely touched:**
- `RfcRag.slnx` (create)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (copy, no changes)
- `src/InfraGate.RfcRag/**/*.cs` (copy all 24 .cs files, no changes)
- `src/InfraGate.RfcRag/Migrations/*.sql` (copy all 4 .sql files, no changes)
- `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` (copy, update ProjectReference path from `..\..\src\` to `..\src\`)
- `tests/InfraGate.RfcRag.Tests/**/*` (copy all 13 test files + 7 test data files, no changes)

**Estimated scope:** Medium

#### Task 2: Create local `Directory.Build.props` with inlined shared settings

**Description:** Create a standalone `Directory.Build.props` that replicates the monorepo root's settings (TargetFramework `net10.0`, analyzers, `TreatWarningsAsErrors`, `Meziantou.Analyzer`) without inheriting from the monorepo. Remove `TargetFramework` from the individual `.csproj` files since it's now inherited.

**Acceptance criteria:**
- [ ] `Directory.Build.props` at repo root with same property values as monorepo root
- [ ] Individual `.csproj` files remove `<TargetFramework>net10.0</TargetFramework>` (now inherited)
- [ ] Build succeeds with same analyzer behavior

**Verification:**
- [ ] `dotnet build RfcRag.slnx` exits 0
- [ ] `dotnet build RfcRag.slnx /warnaserror` exits 0 (same strictness as monorepo)

**Dependencies:** Task 1

**Files likely touched:**
- `Directory.Build.props` (create)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (edit: remove TargetFramework line)
- `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` (edit: remove TargetFramework line)

**Estimated scope:** Small

#### Task 3: Create `.editorconfig` for the new repo

**Description:** Copy the monorepo root `.editorconfig` and adapt the test-specific section path from `[tests/**/*.cs]` to the new layout. Keep all analyzer suppressions intact.

**Acceptance criteria:**
- [ ] `.editorconfig` at repo root with all C# rules from monorepo
- [ ] Test-specific section uses correct path pattern for the new layout
- [ ] Build succeeds with no new analyzer warnings

**Verification:**
- [ ] `dotnet build RfcRag.slnx` exits 0 (no suppressions missing)

**Dependencies:** Task 1

**Files likely touched:**
- `.editorconfig` (create)

**Estimated scope:** Small

### Checkpoint: Foundation

- [ ] Solution builds (`dotnet build RfcRag.slnx`)
- [ ] All unit tests pass (`dotnet test --filter "Category!=Integration"`)

---

### Phase 2: Docker & Deployment

#### Task 4: Adapt Dockerfile for standalone repo

**Description:** Update the Dockerfile from `src/InfraGate.RfcRag/Dockerfile` to work at repo root. Remove `src/InfraGate.RfcRag/` path prefixes. The filter stage copies `src/` directly now; the build stage uses flattened paths.

**Acceptance criteria:**
- [ ] Dockerfile at repo root builds successfully
- [ ] `docker build -t rfc-rag .` succeeds
- [ ] Image runs as stdio MCP server

**Verification:**
- [ ] `docker build -t rfc-rag .` exits 0
- [ ] `docker run --rm rfc-rag` starts without crash (may error on missing env vars, which is expected)

**Dependencies:** Task 1

**Files likely touched:**
- `Dockerfile` (adapt from `src/InfraGate.RfcRag/Dockerfile`)

**Estimated scope:** Small

#### Task 5: Adapt docker-compose and env example for standalone repo

**Description:** Update `rfc-rag.yaml` and `rfc-rag.env.example` for standalone use. Change build context from `../..` to `.`, update dockerfile path from `src/InfraGate.RfcRag/Dockerfile` to `Dockerfile`. Move files to repo root. Remove duplicate `OPENROUTER_API_KEY` env var (keep only `InfraGate__OpenRouter__ApiKey`).

**Acceptance criteria:**
- [ ] `docker-compose.yml` at repo root
- [ ] `.env.rfc-rag.example` at repo root
- [ ] `docker compose up` starts postgres + rfc-rag services

**Verification:**
- [ ] `cp .env.rfc-rag.example .env.rfc-rag && docker compose up` starts both services

**Dependencies:** Task 4

**Files likely touched:**
- `docker-compose.yml` (adapt from `deploy/compose/rfc-rag.yaml`)
- `.env.rfc-rag.example` (adapt from `deploy/compose/rfc-rag.env.example`)

**Estimated scope:** Small

---

### Phase 3: Documentation & Repo Metadata

#### Task 6: Create root README.md and supporting docs

**Description:** Adapt the existing project README (`src/InfraGate.RfcRag/README.md`) to serve as the root repo README. Update the "Extraction from monorepo" section to reflect the new standalone reality. Add badges. Create `LICENSE` (Apache-2.0, same as parent). Create `CHANGELOG.md`, `SECURITY.md`, `CONTRIBUTING.md`.

**Acceptance criteria:**
- [ ] `README.md` at repo root with architecture, quick start, MCP tools, configuration, Docker, and boundaries sections
- [ ] `LICENSE` — Apache-2.0
- [ ] `CHANGELOG.md` — initial entry for first standalone release
- [ ] `SECURITY.md` — standard text
- [ ] `CONTRIBUTING.md` — brief contribution guide

**Verification:**
- [ ] Manual review: README has all sections, links work, docker commands are correct for standalone

**Dependencies:** Task 5

**Files likely touched:**
- `README.md` (create/adapt)
- `LICENSE` (create)
- `CHANGELOG.md` (create)
- `SECURITY.md` (create)
- `CONTRIBUTING.md` (create)

**Estimated scope:** Medium

#### Task 7: Create `.gitignore`

**Description:** Create a `.gitignore` tailored for a .NET project with Docker artifacts. Start from standard `dotnet new gitignore` template plus Docker-specific entries.

**Acceptance criteria:**
- [ ] `.gitignore` excludes `bin/`, `obj/`, `.env.rfc-rag` (not `.env.rfc-rag.example`), `pgdata/`, Docker artifacts

**Verification:**
- [ ] `git init && git add .` does not stage build artifacts or secrets

**Dependencies:** None (can parallelize with Phase 1)

**Files likely touched:**
- `.gitignore` (create)

**Estimated scope:** Small

---

### Phase 4: CI/CD

#### Task 8: Create GitHub Actions CI workflow

**Description:** Create `.github/workflows/ci.yml` that builds the solution, runs unit tests (exclude integration), and runs `dotnet format --verify-no-changes`. Use the monorepo's existing CI as reference but simplify (no SonarCloud, no E2E tests, no Keycloak).

**Acceptance criteria:**
- [ ] Workflow triggers on push/PR to `main`
- [ ] Build job: `dotnet build RfcRag.slnx`
- [ ] Test job: `dotnet test RfcRag.slnx --filter "Category!=Integration"`
- [ ] Docker build job (build only, no push on PR)

**Verification:**
- [ ] Workflow syntax valid (no YAML parse errors)
- [ ] Matches `.github/workflows/ci.yml` pattern from monorepo at reduced scope

**Dependencies:** Task 1

**Files likely touched:**
- `.github/workflows/ci.yml` (create)

**Estimated scope:** Medium

#### Task 9: Create Docker publish workflow (optional)

**Description:** Create `.github/workflows/publish.yml` that builds and pushes the Docker image to GHCR on release/tag. Reference the monorepo's publish workflow for patterns.

**Acceptance criteria:**
- [ ] Workflow triggers on `v*` tags
- [ ] Builds and pushes to `ghcr.io/<owner>/rfc-rag:<tag>` and `ghcr.io/<owner>/rfc-rag:latest`

**Verification:**
- [ ] Workflow syntax valid

**Dependencies:** Task 4

**Files likely touched:**
- `.github/workflows/publish.yml` (create)

**Estimated scope:** Small

### Checkpoint: Complete

- [ ] All acceptance criteria met across all tasks
- [ ] `dotnet build && dotnet test --filter "Category!=Integration"` passes
- [ ] `docker build -t rfc-rag .` succeeds
- [ ] README is reviewer-ready

---

### Phase 5: Monorepo Cleanup (post-extraction)

#### Task 10: Remove RfcRag from k8s-toolkit monorepo

**Description:** After extraction is confirmed working and code is in the new repo, remove all RfcRag traces from the monorepo: solution entries, source/test directories, deploy files, and documentation references.

**Acceptance criteria:**
- [ ] `InfraGate.slnx` no longer references RfcRag projects (remove lines 12 and 40)
- [ ] `src/InfraGate.RfcRag/` directory removed
- [ ] `tests/InfraGate.RfcRag.Tests/` directory removed
- [ ] `deploy/compose/rfc-rag.yaml` and `deploy/compose/rfc-rag.env.example` removed
- [ ] `README.md` and `AGENTS.md` updated to remove RfcRag references
- [ ] Monorepo still builds and all remaining tests pass

**Verification:**
- [ ] `dotnet build InfraGate.slnx` exits 0
- [ ] `dotnet test InfraGate.slnx` passes (minus RfcRag tests)

**Dependencies:** All previous tasks (new repo confirmed working)

**Files likely touched:**
- `InfraGate.slnx` (edit: remove lines 12 and 40)
- `README.md` (edit: remove RfcRag from project map)
- `AGENTS.md` (edit, if RfcRag mentioned)
- `deploy/compose/rfc-rag.yaml` (delete)
- `deploy/compose/rfc-rag.env.example` (delete)

**Estimated scope:** Small (mechanical)

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `Directory.Build.props` differences cause subtle build behavior change | Medium | Compare binlogs before/after; verify analyzer output identical |
| Test project `ProjectReference` path breaks | Low | Relative path changes from `..\..\src\InfraGate.RfcRag\` to `..\src\InfraGate.RfcRag\` (one level shallower) |
| Dockerfile filter stage copies wrong scope | Medium | Verify with `docker build`; filter uses `find src/` so it works unchanged |
| Migration content items path references break | Low | Content items use relative paths within project; no change needed |
| `.editorconfig` missing rules cause new warnings | Low | Run `dotnet build` before/after and compare warning count |
| Monorepo `dotnet test` breaks after removal | Low | No other project depends on RfcRag; verify with grep for ProjectReference |

---

## Open Questions

1. **Repo name:** `rfc-rag` (matching docker compose project) or `InfraGate.RfcRag` (matching .NET project)?
   - Recommendation: `rfc-rag` — shorter, matches existing compose name, no InfraGate tie
2. **Docker compose file location:** Repo root (`docker-compose.yml`) or `deploy/compose/rfc-rag.yaml` (current convention)?
   - Recommendation: repo root since it's a single-project repo
3. **GHCR org/owner:** `mirusser/rfc-rag` or a new org?
4. **Should `.codegraph/` be initialized in the new repo?** Recommendation: yes, after everything is set up

---

## Recommended Execution Order

```
Phase 1 (parallel):  Task 1 + Task 2 + Task 3 + Task 7  →  Checkpoint: Foundation
Phase 2:             Task 4 → Task 5
Phase 3:             Task 6
Phase 4:             Task 8 + Task 9 (parallel)
Phase 5:             Task 10 (only after new repo confirmed working)
```

Tasks 1, 2, 3, and 7 are fully independent and can run in parallel.
