# Implementation Plan: InfraGate.RfcRag — RFC RAG MCP Server

## Overview

Build a local, read-only MCP server that exposes semantic + lexical search over a local RFC mirror
(`~/OtherRepos/rfc-mirror/`, ~9,800 `.txt` files). The server indexes RFCs into PostgreSQL with pgvector
for vector search and PostgreSQL full-text search for lexical retrieval, then exposes MCP tools for
coding agents to search and cite RFCs with section-level precision.

The plan in `.agents/Plans/loose/rfcs-rag-mcp.md` provides the conceptual design. This document
breaks it into implementable, verifiable tasks following the repo's conventions.

---

## Architecture Decisions

### ADR-1: .NET 10 native, not Python
**Rationale**: The repo already uses `ModelContextProtocol` 1.3.0 (proven across 7 projects),
`Microsoft.Extensions.AI` 10.6.0, and PostgreSQL patterns. Introducing Python would create a
separate build system, dependency management, and test framework for no benefit.

### ADR-2: PostgreSQL + pgvector for vector store (not Qdrant/sqlite-vec)
**Rationale**: The repo already has PostgreSQL infrastructure (Approvals.Postgres, AuditOutbox.Postgres).
pgvector is a mature extension with a well-maintained .NET client (`Pgvector` NuGet). Using the same
database reduces operational complexity — no new service to manage.

### ADR-3: PostgreSQL full-text search for lexical retrieval (not SQLite FTS5)
**Rationale**: Since PostgreSQL is already required for vector search, using its built-in `tsvector`/`tsquery`
for lexical search eliminates the second-database dependency. PostgreSQL's English stemming and ranking
(`ts_rank`) are production-grade for RFC text.

### ADR-4: OpenRouter for embeddings (not local Ollama)
**Rationale**: Ollama cannot run locally in the target environment. OpenRouter provides OpenAI-compatible
embedding endpoints (`text-embedding-3-small` at 1536d) through the same API key already used for LLM
chat in Observer/Planner. Embeddings are generated once during indexing (~50k sections), not per-query,
so cost is bounded. `Microsoft.Extensions.AI.OpenAI` already wired in the repo (version 10.6.0).
One-time indexing cost estimate: ~$0.10–0.50 for 50k sections at OpenRouter's embedding pricing.
Query-time: zero embedding cost (only search, not generate).

### ADR-5: Raw Npgsql + Dapper (not EF Core)
**Rationale**: The existing PostgreSQL projects (Approvals.Postgres, AuditOutbox.Postgres) use raw Npgsql
with Dapper, not EF Core. Consistency with the codebase takes priority over EF Core's convenience for
vector operations. The `Pgvector` package works with raw Npgsql via `UseVector()`.

### ADR-6: Section-based chunking (not fixed-size)
**Rationale**: RFCs have predictable structure (numbered sections). Section-based chunks preserve
citation fidelity — agents can cite "RFC 9110 §6.3" instead of "somewhere around token 400".
Large sections get sub-chunked with parent heading preserved.

### ADR-7: stdio MCP server (not HTTP gateway)
**Rationale**: This is a read-only, local-only tool. There's no need for OAuth, guardrails, or
gateway infrastructure. Following the existing `InfraGate.McpServer` stdio pattern is simpler
and lets coding agents invoke it directly.

---

## Resolved Design Questions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `text-embedding-3-small` (1536d) via OpenRouter | Already have API key, OpenAI-compatible, 1536 dimensions provide better semantic resolution. One-time indexing cost ~$0.10-0.50. |
| 2 | Separate `rfc_abnf_blocks` table | Dedicated GIN-indexed `tsvector` column for grammar-specific full-text search, foreign-keyed to `rfc_sections`. ABNF (Augmented Backus-Naur Form) is the formal grammar notation used in IETF RFCs to define protocol syntax precisely (message formats, handshake sequences, wire encodings). |
| 3 | Pre-extracted `normative_occurrences` table | Columns: `rfc_number`, `section_id`, `keyword`, `line_offset`. B-tree index on `(keyword, rfc_number)` for sub-50ms queries. |
| 4 | Auto-index (sync) on every MCP server start | Server blocks until indexing completes, then starts listening. Incremental SHA256-based skip means subsequent starts are fast (seconds, not minutes). No separate CLI command needed. First run indexes everything (~10-15 min for ~9,800 RFCs). |

---

## Task List

### Phase 1: Foundation — Project Scaffold & Data Model

---

#### Task 1: Scaffold `InfraGate.RfcRag` project ✓

**Description:** Create the .NET project with correct conventions: `.csproj`, `GlobalUsings.cs`,
`README.md`, and register it in `InfraGate.slnx`. No logic yet — just the skeleton.

**Acceptance criteria:**
- [x] `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` targets `net10.0`, inherits root `Directory.Build.props`
- [x] `<InternalsVisibleTo Include="InfraGate.RfcRag.Tests" />` present
- [x] `src/InfraGate.RfcRag/GlobalUsings.cs` with initial empty set (expand as needed)
- [x] `src/InfraGate.RfcRag/README.md` follows repo format (Title, Description, Owns, Contents, Boundaries)
- [x] Project added to `/InfraGate.slnx` under `src/` folder
- [x] `dotnet build src/InfraGate.RfcRag/` succeeds (warnings-as-errors clean)

**Dependencies:** None

**Files:**
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (new) ✓
- `src/InfraGate.RfcRag/GlobalUsings.cs` (new) ✓
- `src/InfraGate.RfcRag/README.md` (new) ✓
- `InfraGate.slnx` (edit) ✓

**Status:** ✅ Complete

---

#### Task 2: Scaffold `InfraGate.RfcRag.Tests` test project ✓

**Description:** Create the xUnit test project following the `tests/InfraGate.*.Tests` convention.

**Acceptance criteria:**
- [x] `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` with xUnit, coverlet, Test.Sdk
- [x] Project reference to `src/InfraGate.RfcRag/`
- [x] `tests/InfraGate.RfcRag.Tests/README.md` following test-readme format
- [x] Project added to `/InfraGate.slnx` under `tests/` folder
- [x] `dotnet test tests/InfraGate.RfcRag.Tests/` succeeds (no tests yet, zero-test pass)

**Dependencies:** Task 1

**Status:** ✅ Complete

---

#### Task 3: Define data model — chunk record, conventions, SQL migration ✓

**Description:** Define the PostgreSQL schema and C# types for RFC sections (chunks), RFC metadata,
and the vector store. Write the initial SQL migration following the repo's migration pattern.

**Acceptance criteria:**
- [x] `RfcRagConventions.cs` — constants for schema name (`rfc_rag`), table names, lock keys, migrations directory
- [x] `RfcSection.cs` — sealed record class matching the chunk shape (Id, RfcNumber, Title, Section, Heading, Text, SourcePath, Url, Embedding)
- [x] `RfcMetadata.cs` — sealed record class for RFC header metadata (number, title, date, status, obsoletes, updates)
- [x] `RfcAbnfBlock.cs` — sealed record class for ABNF grammar blocks with RuleNames array
- [x] `NormativeOccurrence.cs` — sealed record class for normative keyword tracking
- [x] `RfcDocument.cs` — sealed record class aggregating all parsed data
- [x] `Migrations/0001-initial-rfc-rag-schema.sql`:
  - `CREATE EXTENSION IF NOT EXISTS vector;`
  - `rfc_rag.rfc_sections` table with `embedding vector(1536)`, `search_vector tsvector`, HNSW + GIN indexes
  - `rfc_rag.indexed_rfcs` table for SHA256-based incremental indexing
  - `rfc_rag.rfc_abnf_blocks` with dedicated GIN-indexed tsvector
  - `rfc_rag.normative_occurrences` with B-tree index on (keyword, rfc_number)
- [x] `RfcRagMigrationRunner.cs` — applies migrations with advisory lock + checksum verification, following `PostgresApprovalMigrationRunner` pattern
- [x] `dotnet build src/InfraGate.RfcRag/` succeeds (warnings-as-errors clean)

**Dependencies:** Task 1

**Files:**
- `src/InfraGate.RfcRag/RfcRagConventions.cs` (new) ✓
- `src/InfraGate.RfcRag/Models/RfcSection.cs` (new) ✓
- `src/InfraGate.RfcRag/Models/RfcMetadata.cs` (new) ✓
- `src/InfraGate.RfcRag/Models/RfcAbnfBlock.cs` (new) ✓
- `src/InfraGate.RfcRag/Models/NormativeOccurrence.cs` (new) ✓
- `src/InfraGate.RfcRag/RfcDocument.cs` (new) ✓
- `src/InfraGate.RfcRag/RfcRagMigrationRunner.cs` (new) ✓
- `src/InfraGate.RfcRag/Migrations/0001-initial-rfc-rag-schema.sql` (new) ✓

**Status:** ✅ Complete

---

### Phase 2: Core — RFC Parser & Indexer

---

#### Task 4: Build the RFC parser

**Description:** Parse raw RFC `.txt` files: strip page headers/footers/form-feeds, extract
front-matter metadata (RFC number, title, date, category, obsoletes/updates), split into
sections by heading patterns, extract normative keywords (MUST, MUST NOT, SHOULD, etc.),
extract ABNF blocks.

**Acceptance criteria:**
- [x] `RfcParser.cs` — entry point `ParseAsync` returning `RfcDocument`
- [x] `RfcDocument.cs` — holds metadata + list of sections + ABNF blocks + normative occurrences
- [x] Page header/footer stripping: removes lines matching `[Page N]` patterns, form feeds, and RFC header block
- [x] Section splitting: detects `N. `, `N.N. `, `N.N.N. `, `Appendix N. ` patterns
- [x] Metadata extraction: RFC number from filename + header, title from front matter, date, category, obsoletes, updates
- [x] Normative keyword extraction: finds MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL with word-boundary matching
- [x] ABNF block detection: identifies indented blocks containing `=` and rule-name patterns
- [x] Unit tests pass: RFC 2119 (metadata, sections, normative keywords)
- [x] Additional test fixtures: RFC 9110 (complex multi-section), RFC 8446 (TLS with subsections)

**Dependencies:** Task 2 (test project exists)

**Files:**
- `src/InfraGate.RfcRag/RfcParser.cs` (new) ✓
- `src/InfraGate.RfcRag/RfcDocument.cs` (new) ✓
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcParserTests.cs` (new) ✓
- `tests/InfraGate.RfcRag.Tests/TestData/` — fixture RFC files (new) ✓

**Verification:**
- [x] `dotnet test tests/InfraGate.RfcRag.Tests/ --filter RfcParserTests` — all 5 pass
- [x] Parse RFC 9110: correct title "HTTP Semantics", section "6.3" found

---

#### Task 5: Build the embedding generator integration

**Description:** Wire up `IEmbeddingGenerator<string, Embedding<float>>` using OpenRouter's
OpenAI-compatible embedding API. Follows the existing `ChatClientFactory` pattern from
InfraGate.Planner. Creates a service that takes text chunks and returns vector embeddings,
handling batching and rate-limit retry.

**Acceptance criteria:**
- [ ] `EmbeddingGeneratorFactory.cs` — creates `IEmbeddingGenerator<string, Embedding<float>>` using `OpenAIClient` pointed at OpenRouter endpoint (`https://openrouter.ai/api/v1`)
- [ ] Uses existing `InfraGate__OpenRouter__ApiKey` for authentication
- [ ] Configured via `RfcRagOptions.EmbeddingModel` (default: `openai/text-embedding-3-small`)
- [ ] `EmbeddingService.cs` — batches text chunks (configurable batch size, default 20 for OpenRouter limits), generates embeddings, maps to `float[]`
- [ ] Handles rate limiting with exponential backoff (reuses `RateLimitRetryingChatClient` pattern or equivalent for embeddings)
- [ ] Unit tests: mock `IEmbeddingGenerator`, verify batching logic, verify error handling
- [ ] DI registration via `ServiceCollectionExtensions.AddRfcRagEmbeddings()`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.RfcRag/EmbeddingService.cs` (new)
- `src/InfraGate.RfcRag/EmbeddingGeneratorFactory.cs` (new)
- `src/InfraGate.RfcRag/Settings/RfcRagOptions.cs` (new)
- `src/InfraGate.RfcRag/ServiceCollectionExtensions.cs` (new)
- `tests/InfraGate.RfcRag.Tests/UnitTests/EmbeddingServiceTests.cs` (new)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (add packages)

**Estimated scope:** M

---

#### Task 6: Build the RFC indexer

**Description:** The main indexing pipeline. Walks the RFC mirror directory, parses each RFC,
generates embeddings for each section, and stores everything in PostgreSQL. Uses Dapper + Npgsql
following the repo's existing PostgreSQL access patterns.

**Acceptance criteria:**
- [ ] `RfcIndexer.cs` — orchestrates: walk → parse → embed → store
- [ ] `IIndexerService` with `IndexAllAsync(cancellationToken)` and incremental SHA256-based skip
- [ ] Walks `~/OtherRepos/rfc-mirror/` (configurable via `RfcRagOptions.RfcMirrorPath`)
- [ ] `RfcRepository.cs` — Dapper-based CRUD for `rfc_sections` table:
  - `InsertSectionsAsync`, `GetByRfcNumberAsync`, `DeleteByRfcNumberAsync`
  - `SearchLexicalAsync` — uses `ts_query` + `ts_rank`
  - `SearchVectorAsync` — uses `<=>` cosine distance
  - `SearchHybridAsync` — combined lexical + vector with reciprocal rank fusion
  - `SearchAbnfAsync` — FTS on `rfc_abnf_blocks`
  - `SearchNormativeAsync` — indexed lookup on `normative_occurrences`
- [ ] `SearchService.cs` — wraps repository and returns formatted SearchResult records
- [ ] Unit tests for repository with Testcontainers PostgreSQL
- [ ] Unit tests for indexer with fixture mirror directory

**Dependencies:** Tasks 3, 4, 5

**Files likely touched:**
- `src/InfraGate.RfcRag/RfcIndexer.cs` (new)
- `src/InfraGate.RfcRag/IIndexerService.cs` (new)
- `src/InfraGate.RfcRag/RfcRepository.cs` (new)
- `src/InfraGate.RfcRag/SearchService.cs` (new)
- `src/InfraGate.RfcRag/SearchResult.cs` (new)
- `tests/InfraGate.RfcRag.Tests/IntegrationTests/RfcRepositoryTests.cs` (new)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcIndexerTests.cs` (new)

**Estimated scope:** L

---

### Checkpoint: Core Indexing Works
- [ ] Full index of 5 fixture RFCs completes without errors
- [ ] `SearchLexicalAsync("HTTP semantics")` returns RFC 9110 sections ranked correctly
- [ ] `SearchVectorAsync(embedding)` returns relevant sections
- [ ] Incremental re-index skips unchanged RFCs
- [ ] All unit + integration tests pass

---

### Phase 3: MCP Server — Agent Interface

---

#### Task 7: Build the MCP server with tools

**Description:** Create the stdio MCP server bootstrapped as a Generic Host, registering MCP tools
that expose search, retrieval, and metadata lookup to coding agents.

**MCP tools:**
- `search_rfc` — hybrid search (lexical + vector), returns section metadata + excerpts
- `get_rfc` — full RFC text retrieval
- `get_rfc_section` — precise section lookup by number
- `search_normative` — normative keyword search (MUST, SHOULD, etc.)
- `search_abnf` — ABNF grammar search by rule name or fragment
- `find_updates_obsoletes` — RFC relationship lookup
- `rfc_stats` — index statistics (count, dates, model)

**Acceptance criteria:**
- [ ] `Program.cs` — Generic Host bootstrapping following `InfraGate.McpServer` pattern with `AddMcpServer()`, `WithStdioServerTransport()`, `WithToolsFromAssembly()`
- [ ] `RfcRagTools.cs` — static class with `[McpServerToolType]` and `[McpServerTool]` methods
- [ ] `ServiceCollectionExtensions.AddRfcRagServices()` — registers all services in DI
- [ ] Configuration via environment variables:
  - `InfraGate__RfcRag__RfcMirrorPath`
  - `InfraGate__RfcRag__PostgresConnectionString`
  - `InfraGate__RfcRag__EmbeddingModel` (default: `openai/text-embedding-3-small`)
  - `InfraGate__OpenRouter__ApiKey` (reuses existing)
- [ ] Auto-index on startup: blocks until indexing completes, incremental SHA256 skip
- [ ] `README.md` documents all tools, configuration, and local run instructions

**Dependencies:** Task 6

**Files likely touched:**
- `src/InfraGate.RfcRag/Program.cs` (new)
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (new)
- `src/InfraGate.RfcRag/Settings/RfcRagOptions.cs` (edit)
- `src/InfraGate.RfcRag/ServiceCollectionExtensions.cs` (edit)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (add `ModelContextProtocol` reference)

**Estimated scope:** L

---

### Checkpoint: MCP Server Works End-to-End
- [ ] `search_rfc("HTTP content negotiation")` returns relevant RFC 9110 sections
- [ ] `get_rfc_section(9110, "6.3")` returns exact section text
- [ ] `search_normative("MUST NOT")` returns sections with normative MUST NOT
- [ ] `search_abnf("request-line")` returns ABNF definitions
- [ ] `find_updates_obsoletes(9110)` shows correct relationships
- [ ] MCP server connects successfully from Claude Code / Codex

---

### Phase 4: Polish — Tests, Docs & Validation

---

#### Task 8: Write integration tests

**Description:** Write integration tests that require a real PostgreSQL instance (Testcontainers)
and a real RFC mirror fixture. These verify end-to-end: parse → index → search.

**Acceptance criteria:**
- [ ] `RfcRagIntegrationTests.cs` — Testcontainers PostgreSQL with pgvector extension
- [ ] `IndexAndSearch_ReturnsRelevantResults` — index 5 fixture RFCs, search, verify ranking
- [ ] `SectionLookup_ReturnsExactMatch` — verify `get_rfc_section` precision
- [ ] `NormativeSearch_FindsCorrectKeywords` — verify keyword extraction accuracy
- [ ] `IncrementalIndex_SkipsUnchanged` — verify SHA256-based skip logic
- [ ] Integration tests tagged with `[Trait("Category", "Integration")]`

**Dependencies:** Task 7

**Estimated scope:** M

---

#### Task 9: Documentation and final verification

**Description:** Finalize all documentation, run the full pipeline against the real RFC mirror,
verify all tools work end-to-end, fix any issues.

**Acceptance criteria:**
- [ ] `README.md` complete: setup instructions, configuration, tool reference, example queries
- [ ] Full index of all ~9,800 RFCs completes without errors
- [ ] Performance benchmark: `search_rfc` returns in < 500ms P95
- [ ] Performance benchmark: `get_rfc_section` returns in < 50ms
- [ ] All unit + integration tests pass
- [ ] Build succeeds with warnings-as-errors
- [ ] Manual smoke test: connect Claude Code to the MCP server

**Dependencies:** Tasks 7, 8

**Estimated scope:** S

---

### Checkpoint: Complete
- [ ] All acceptance criteria met
- [ ] Ready for review
- [ ] Agent can answer RFC questions with precise citations

---

## Dependency Graph

```
Task 1 (scaffold src) ──┬── Task 3 (data model + migration) ✓
                        │       │
                        ├── Task 5 (embeddings) ——┐
                        │                          │
Task 2 (scaffold tests) ─┼── Task 4 (RFC parser) ✓─┤
                        │                          │
                        │       └── Task 6 (indexer + search) ──┬── Task 7 (MCP server)
                        │                                       │
                        │                                       └── Task 8 (integration tests)
                        │                                               │
                        └── Task 9 (docs + verify) ◄────────────────────┘
```

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| OpenRouter API key missing or invalid | High | Check at startup; clear error message with setup instructions; reuse existing `InfraGate__OpenRouter__ApiKey` env var |
| ~9,800 RFCs take too long to embed | Medium | Batch embedding (20 at a time, OpenRouter limit), progress logging, incremental SHA256 skip |
| OpenRouter rate limiting during indexing | Medium | Exponential backoff retry; configurable concurrency |
| Some RFCs have non-standard formatting | Medium | Parser tested against diverse sample (early RFCs, modern RFCs, short, long) |
| pgvector extension not available in user's PostgreSQL | Low | Migration runner checks + clear error message; Docker Compose with pgvector-enabled image documented |
| Embedding dimension mismatch | Medium | `RfcRagOptions` configures both schema creation and runtime; validation at startup |

## Configuration Reference

| Env var | Default | Description |
|---------|---------|-------------|
| `InfraGate__RfcRag__RfcMirrorPath` | `~/OtherRepos/rfc-mirror/` | Path to local RFC mirror |
| `InfraGate__RfcRag__PostgresConnectionString` | (required) | PostgreSQL connection string |
| `InfraGate__RfcRag__EmbeddingModel` | `openai/text-embedding-3-small` | OpenRouter embedding model |
| `InfraGate__RfcRag__RunMigrationsOnStartup` | `true` | Auto-apply SQL migrations |
| `InfraGate__OpenRouter__ApiKey` | (reuses existing) | OpenRouter API key |

## Database Schema

```
rfc_rag.rfc_sections          — primary search unit (vectors + FTS)
rfc_rag.indexed_rfcs           — SHA256 tracking for incremental indexing
rfc_rag.rfc_abnf_blocks        — extracted ABNF grammar blocks
rfc_rag.normative_occurrences  — pre-extracted normative keywords
rfc_rag.schema_migrations      — applied migration tracking
```
