# Implementation Plan: InfraGate.RfcRag — RFC RAG MCP Server

## Overview

Build a local, read-only MCP server that exposes semantic + lexical search over a local RFC mirror
(`~/OtherRepos/rfc-mirror/`, ~9,800 `.txt` files). The server indexes RFCs into PostgreSQL with pgvector
for vector search and PostgreSQL full-text search for lexical retrieval, then exposes MCP tools for
coding agents to search and cite RFCs with section-level precision.

The plan in `.agents/Plans/loose/rfcs-rag-mcp.md` provides the conceptual design. This document
breaks it into implementable, verifiable tasks following the repo's conventions.

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

## Task List

### Phase 1: Foundation — Project Scaffold & Data Model

---

#### Task 1: Scaffold `InfraGate.RfcRag` project

**Description:** Create the .NET project with correct conventions: `.csproj`, `GlobalUsings.cs`,
`README.md`, and register it in `InfraGate.slnx`. No logic yet — just the skeleton.

**Acceptance criteria:**
- [ ] `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` targets `net10.0`, inherits root `Directory.Build.props`
- [ ] `<InternalsVisibleTo Include="InfraGate.RfcRag.Tests" />` present
- [ ] `src/InfraGate.RfcRag/GlobalUsings.cs` with initial empty set (expand as needed)
- [ ] `src/InfraGate.RfcRag/README.md` follows repo format (Title, Description, Owns, Contents, Boundaries)
- [ ] Project added to `/InfraGate.slnx` under `src/` folder
- [ ] `dotnet build src/InfraGate.RfcRag/` succeeds (warnings-as-errors clean)

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (new)
- `src/InfraGate.RfcRag/GlobalUsings.cs` (new)
- `src/InfraGate.RfcRag/README.md` (new)
- `InfraGate.slnx` (edit)

**Estimated scope:** S

---

#### Task 2: Scaffold `InfraGate.RfcRag.Tests` test project

**Description:** Create the xUnit test project following the `tests/InfraGate.*.Tests` convention.

**Acceptance criteria:**
- [ ] `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` with xUnit, coverlet, Test.Sdk
- [ ] Project reference to `src/InfraGate.RfcRag/`
- [ ] `tests/InfraGate.RfcRag.Tests/README.md` following test-readme format
- [ ] Project added to `/InfraGate.slnx` under `tests/` folder
- [ ] `dotnet test tests/InfraGate.RfcRag.Tests/` succeeds (no tests yet, zero-test pass)

**Dependencies:** Task 1

**Files likely touched:**
- `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` (new)
- `tests/InfraGate.RfcRag.Tests/README.md` (new)
- `InfraGate.slnx` (edit)

**Estimated scope:** S

---

#### Task 3: Define data model — chunk record, conventions, SQL migration

**Description:** Define the PostgreSQL schema and C# types for RFC sections (chunks), RFC metadata,
and the vector store. Write the initial SQL migration following the repo's migration pattern.

**Acceptance criteria:**
- [ ] `RfcRagConventions.cs` — constants for schema name (`rfc_rag`), table names, lock keys, migrations directory
- [ ] `RfcSection.cs` — record/class matching the chunk shape from the concept plan (`Id`, `RfcNumber`, `Title`, `Section`, `Heading`, `Text`, `SourcePath`, `Url`)
- [ ] `RfcMetadata.cs` — record for RFC header metadata (number, title, date, status, obsoletes, updates)
- [ ] `Migrations/0001-initial-rfc-rag-schema.sql`:
  - `CREATE EXTENSION IF NOT EXISTS vector;`
  - `data.rfc_sections` table with `id`, `rfc_number`, `title`, `section`, `heading`, `text`, `source_path`, `url`, `embedding vector(N)`, `search_vector tsvector`
  - GIN index on `search_vector` for full-text search
  - HNSW index on `embedding` with `vector_cosine_ops`
  - `rfc_rag.schema_migrations` table
- [ ] `RfcRagMigrationRunner.cs` — applies migrations with advisory lock + checksum verification, following `PostgresApprovalMigrationRunner` pattern
- [ ] Build succeeds with warnings-as-errors clean

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.RfcRag/RfcRagConventions.cs` (new)
- `src/InfraGate.RfcRag/RfcSection.cs` (new)
- `src/InfraGate.RfcRag/RfcMetadata.cs` (new)
- `src/InfraGate.RfcRag/RfcRagMigrationRunner.cs` (new)
- `src/InfraGate.RfcRag/Migrations/0001-initial-rfc-rag-schema.sql` (new)

**Estimated scope:** M

---

### Phase 2: Core — RFC Parser & Indexer

---

#### Task 4: Build the RFC parser

**Description:** Parse raw RFC `.txt` files: strip page headers/footers/form-feeds, extract
front-matter metadata (RFC number, title, date, status, obsoletes/updates), split into
sections by heading patterns, extract normative keywords (MUST, MUST NOT, SHOULD, etc.),
extract ABNF blocks.

**Acceptance criteria:**
- [ ] `RfcParser.cs` — entry point accepting a file path, returning parsed `RfcDocument`
- [ ] `RfcDocument.cs` — holds metadata + list of sections
- [ ] Page header/footer stripping: removes lines matching `RFC XXXX  Title  Month Year` and `[Page N]` patterns
- [ ] Form feed (`\f`) characters removed
- [ ] Section splitting: detects `1.  Heading`, `1.1.  Subheading`, `Appendix A.  Title` patterns
- [ ] Metadata extraction: RFC number from filename + header, title from first header block, date from header, status/obsoletes/updates from header lines
- [ ] Normative keyword extraction: finds `MUST`, `MUST NOT`, `REQUIRED`, `SHALL`, `SHALL NOT`, `SHOULD`, `SHOULD NOT`, `RECOMMENDED`, `MAY`, `OPTIONAL` in running text
- [ ] ABNF block detection: identifies indented blocks containing `=` and ABNF syntax patterns
- [ ] Handles edge cases: RFCs with no sections, malformed headers, unusual formatting
- [ ] Unit tests: `RfcParserTests` with at least 5 real RFC files (9110, 8446, 9000, 2119, 3986) + edge case tests

**Dependencies:** Task 2 (test project exists)

**Files likely touched:**
- `src/InfraGate.RfcRag/RfcParser.cs` (new)
- `src/InfraGate.RfcRag/RfcDocument.cs` (new)
- `tests/InfraGate.RfcRag.Tests/RfcParserTests.cs` (new)
- `tests/InfraGate.RfcRag.Tests/TestData/` — small fixture RFC files (new)

**Estimated scope:** L — but unavoidable; this is the hardest single piece

**Verification:**
- [ ] `dotnet test tests/InfraGate.RfcRag.Tests/ --filter RfcParserTests` passes
- [ ] Parse RFC 9110 and verify: correct title "HTTP Semantics", correct section count, correct §6.3 heading

---

#### Task 5: Build the embedding generator integration

**Description:** Wire up `IEmbeddingGenerator<string, Embedding<float>>` using OpenRouter's
OpenAI-compatible embedding API. Follows the existing `ChatClientFactory` pattern from
InfraGate.Planner. Creates a service that takes text chunks and returns vector embeddings,
handling batching and rate-limit retry.

**Acceptance criteria:**
- [ ] `EmbeddingGeneratorFactory.cs` — creates `IEmbeddingGenerator<string, Embedding<float>>` using `OpenAIClient` pointed at OpenRouter endpoint (`https://openrouter.ai/api/v1`)
- [ ] Uses existing `OpenRouterOptions` (or shared `InfraGate__OpenRouter__ApiKey`) for authentication
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
- `tests/InfraGate.RfcRag.Tests/EmbeddingServiceTests.cs` (new)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (add packages)

**Estimated scope:** M

---

#### Task 6: Build the RFC indexer

**Description:** The main indexing pipeline. Walks the RFC mirror directory, parses each RFC,
generates embeddings for each section, and stores everything in PostgreSQL. Uses Dapper + Npgsql
following the repo's existing PostgreSQL access patterns.

**Acceptance criteria:**
- [ ] `RfcIndexer.cs` — orchestrates: walk → parse → embed → store
- [ ] `RfcIndexerService.cs` — `IIndexerService` with `IndexAllAsync(cancellationToken)` and `IndexSingleAsync(rfcNumber, cancellationToken)`
- [ ] Walks `~/OtherRepos/rfc-mirror/` (configurable via `RfcRagOptions.RfcMirrorPath`)
- [ ] Skips already-indexed RFCs (compares SHA256 of source file against stored hash) — incremental indexing
- [ ] `RfcRepository.cs` — Dapper-based CRUD for `rfc_sections` table:
  - `InsertSectionsAsync(IReadOnlyList<RfcSection>, NpgsqlConnection, NpgsqlTransaction)`
  - `GetByRfcNumberAsync(int rfcNumber)`
  - `DeleteByRfcNumberAsync(int rfcNumber)` (for re-indexing)
  - `SearchLexicalAsync(string query, int limit)` — uses `ts_query` + `ts_rank`
  - `SearchVectorAsync(float[] embedding, int limit)` — uses `<=>` cosine distance
  - `SearchHybridAsync(string query, float[] embedding, int limit)` — combined lexical + vector with reciprocal rank fusion
- [ ] `SearchService.cs` — `ISearchService` that wraps the repository and returns formatted SearchResult records with provenance metadata
- [ ] Unit tests for repository with Testcontainers PostgreSQL (Docker available in CI)
- [ ] Unit tests for indexer with a small fixture mirror directory

**Dependencies:** Tasks 3, 4, 5

**Files likely touched:**
- `src/InfraGate.RfcRag/RfcIndexer.cs` (new)
- `src/InfraGate.RfcRag/RfcIndexerService.cs` (new)
- `src/InfraGate.RfcRag/RfcRepository.cs` (new)
- `src/InfraGate.RfcRag/SearchService.cs` (new)
- `src/InfraGate.RfcRag/SearchResult.cs` (new)
- `tests/InfraGate.RfcRag.Tests/RfcIndexerTests.cs` (new)
- `tests/InfraGate.RfcRag.Tests/RfcRepositoryTests.cs` (new)
- `tests/InfraGate.RfcRag.Tests/Fixtures/` — fixture mirror directory (new)

**Estimated scope:** L

---

### Checkpoint: Core Indexing Works
- [ ] Full index of 5 fixture RFCs completes without errors
- [ ] `SearchLexicalAsync("HTTP semantics")` returns RFC 9110 sections ranked correctly
- [ ] `SearchVectorAsync(embedding)` returns relevant sections
- [ ] Incremental re-index skips unchanged RFCs
- [ ] All unit tests pass

---

### Phase 3: MCP Server — Agent Interface

---

#### Task 7: Build the MCP server with tools

**Description:** Create the stdio MCP server bootstrapped as a Generic Host, registering MCP tools
that expose search, retrieval, and metadata lookup to coding agents.

**Acceptance criteria:**
- [ ] `Program.cs` — Generic Host bootstrapping following `InfraGate.McpServer` pattern:
  - `Host.CreateApplicationBuilder(args)` with configuration, DI, and `AddMcpServer()`
  - `WithStdioServerTransport()`
  - `WithToolsFromAssembly()` to discover `[McpServerTool]` methods
- [ ] `RfcRagTools.cs` — static class decorated with `[McpServerToolType]`, containing these tools:

  **`search_rfc`** — hybrid search (lexical + vector)
  - Parameters: `string query`, `int limit = 10`
  - Returns JSON array of `SearchResult` with section metadata and excerpt text (capped at 500 chars per result)
  - Description explains it does both keyword and semantic search

  **`get_rfc`** — full RFC retrieval
  - Parameters: `int rfcNumber`
  - Returns full RFC text with section markers preserved
  - Returns error message if RFC not indexed

  **`get_rfc_section`** — precise section lookup
  - Parameters: `int rfcNumber`, `string section` (e.g., "6.3", "4.1.2")
  - Returns the exact section text with heading
  - Returns error if section not found

  **`search_normative`** — normative keyword search
  - Parameters: `string keyword` (e.g., "MUST", "SHOULD NOT"), `int[]? rfcNumbers = null`
  - Returns sections containing the normative keyword, scoped to specified RFCs or all indexed
  - Description lists valid keywords: MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL

  **`search_abnf`** — ABNF grammar search
  - Parameters: `string query` (ABNF rule name or fragment), `int[]? rfcNumbers = null`
  - Returns ABNF blocks matching the query, with surrounding context
  - Uses PostgreSQL full-text search on the ABNF content stored in a separate `rfc_abnf_blocks` column or table

  **`find_updates_obsoletes`** — relationship lookup
  - Parameters: `int rfcNumber`
  - Returns JSON object: `{ updated_by: [...], obsoleted_by: [...], updates: [...], obsoletes: [...] }`
  - Derived from parsed metadata during indexing

  **`rfc_stats`** — index statistics
  - Parameters: none
  - Returns count of indexed RFCs, total sections, last index date, embedding model used
  
  **Bonus tools** (optional, nice-to-have):
  - `list_indexed_rfcs` — returns all indexed RFC numbers and titles (paginated)
  - `get_rfc_metadata` — returns full metadata for an RFC (title, date, status, relationships)

- [ ] `ServiceCollectionExtensions.AddRfcRagServices()` — registers all services:
  - `RfcRagOptions` from configuration section `InfraGate:RfcRag`
  - `NpgsqlDataSource` as singleton (with `UseVector()`)
  - `RfcRagMigrationRunner` as singleton
  - `IEmbeddingGenerator<string, Embedding<float>>` as singleton
  - `EmbeddingService`, `ISearchService`, `IIndexerService` as singletons
  - Runs migrations on startup if `RfcRagOptions.RunMigrationsOnStartup == true`
- [ ] Configuration via environment variables (reuses `InfraGate__OpenRouter__ApiKey` from existing setup):
  - `InfraGate__RfcRag__RfcMirrorPath`
  - `InfraGate__RfcRag__PostgresConnectionString`
  - `InfraGate__RfcRag__EmbeddingModel` (default: `openai/text-embedding-3-small`)
- [ ] `README.md` documents all tools, configuration, and local run instructions

**Dependencies:** Task 6

**Files likely touched:**
- `src/InfraGate.RfcRag/Program.cs` (new)
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (new)
- `src/InfraGate.RfcRag/Settings/RfcRagOptions.cs` (edit)
- `src/InfraGate.RfcRag/ServiceCollectionExtensions.cs` (edit)
- `src/InfraGate.RfcRag/README.md` (edit)
- `src/InfraGate.RfcRag/InfraGate.RfcRag.csproj` (add `ModelContextProtocol` reference)

**Estimated scope:** L

---

#### Task 8: Auto-index on MCP server startup

**Description:** The MCP server automatically runs the indexer synchronously on every startup
before accepting connections. Uses incremental SHA256-based checks to skip already-indexed
RFCs, so subsequent starts are fast (seconds).

**Acceptance criteria:**
- [ ] `Program.cs` calls `IIndexerService.IndexAllAsync(cancellationToken)` synchronously before `app.RunAsync()`
- [ ] A `--reindex` CLI argument triggers a full drop-and-reindex (requires `--force` confirmation)
- [ ] Indexing progress is logged: "Indexing RFC 5234 (152/9782)..."
- [ ] On first run with empty database, indexes all ~9,800 RFCs and then starts MCP server
- [ ] On subsequent runs, skips unchanged RFCs and starts within seconds
- [ ] If the RFC mirror directory is missing or empty, logs a clear error and exits (doesn't hang)
- [ ] If PostgreSQL is unreachable, logs a clear error with connection string hint and exits

**Dependencies:** Tasks 6, 7

**Files likely touched:**
- `src/InfraGate.RfcRag/Program.cs` (edit)

**Estimated scope:** S

---

### Checkpoint: MCP Server Works End-to-End
- [ ] `dotnet run -- index` indexes all ~9,800 RFCs without errors
- [ ] `search_rfc("HTTP content negotiation")` returns relevant RFC 9110 sections
- [ ] `get_rfc_section(9110, "6.3")` returns exact section text
- [ ] `search_normative("MUST NOT")` returns sections with normative MUST NOT
- [ ] `search_abnf("request-line")` returns ABNF definitions
- [ ] `find_updates_obsoletes(9110)` shows correct relationships
- [ ] MCP server connects successfully from Claude Code / Codex

---

### Phase 4: Polish — Tests, Docs & Validation

---

#### Task 9: Write integration tests

**Description:** Write integration tests that require a real PostgreSQL instance (Testcontainers)
and a real RFC mirror fixture. These verify end-to-end: parse → index → search.

**Acceptance criteria:**
- [ ] `RfcRagIntegrationTests.cs` — Testcontainers PostgreSQL with pgvector extension
- [ ] `IndexAndSearch_ReturnsRelevantResults` — index 5 fixture RFCs, search, verify ranking
- [ ] `SectionLookup_ReturnsExactMatch` — verify `get_rfc_section` precision
- [ ] `NormativeSearch_FindsCorrectKeywords` — verify MUST/SHOULD/etc. extraction accuracy
- [ ] `AbnfSearch_FindsGrammarBlocks` — verify ABNF detection works
- [ ] `IncrementalIndex_SkipsUnchanged` — verify SHA256-based skip logic
- [ ] Integration tests tagged with `[Trait("Category", "Integration")]`
- [ ] Document in test README that these require Docker

**Dependencies:** Task 7

**Files likely touched:**
- `tests/InfraGate.RfcRag.Tests/RfcRagIntegrationTests.cs` (new)
- `tests/InfraGate.RfcRag.Tests/README.md` (edit)
- `tests/InfraGate.RfcRag.Tests/InfraGate.RfcRag.Tests.csproj` (add Testcontainers package)

**Estimated scope:** M

---

#### Task 10: Documentation and final verification

**Description:** Finalize all documentation, run the full pipeline against the real RFC mirror,
verify all tools work end-to-end, fix any issues.

**Acceptance criteria:**
- [ ] `README.md` complete: setup instructions, configuration, tool reference, example queries
- [ ] Full index of all ~9,800 RFCs completes without errors
- [ ] Performance benchmark: `search_rfc` returns in < 500ms P95
- [ ] Performance benchmark: `get_rfc_section` returns in < 50ms
- [ ] All unit + integration tests pass: `dotnet test tests/InfraGate.RfcRag.Tests/`
- [ ] Build succeeds with warnings-as-errors
- [ ] Manual smoke test: connect Claude Code to the MCP server, ask "What does RFC 9110 say about content negotiation?" and verify it cites §8.6 correctly

**Dependencies:** Tasks 8, 9

**Files likely touched:**
- `src/InfraGate.RfcRag/README.md` (edit)
- `tests/InfraGate.RfcRag.Tests/README.md` (edit)

**Estimated scope:** S

---

### Checkpoint: Complete
- [ ] All acceptance criteria met
- [ ] Ready for review
- [ ] Agent can answer RFC questions with precise citations

---

## Dependency Graph

```
Task 1 (scaffold src) ──┬── Task 3 (data model + migration)
                        │       │
                        ├── Task 5 (embeddings)       │
                        │       │                     │
Task 2 (scaffold tests) ─┼── Task 4 (RFC parser)      │
                        │       │                     │
                        │       ├── Task 6 (indexer + search) ──┬── Task 7 (MCP server)
                        │       │                               │       │
                        │       │                               │       ├── Task 8 (CLI)
                        │       │                               │       │       │
                        │       │                               │       └── Task 9 (integration tests)
                        │       │                               │               │
                        │       │                               └── Task 10 (docs + verify)
```

**Parallelizable pairs:** Tasks 3+4+5 can run in parallel after scaffolding. Tasks 8+9 can run in parallel after MCP server.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| OpenRouter API key missing or invalid | High | Check at startup; clear error message with setup instructions; reuse existing `InfraGate__OpenRouter__ApiKey` env var |
| ~9,800 RFCs take too long to embed | Medium | Batch embedding (20 at a time, OpenRouter limit), progress logging, incremental SHA256 skip means only new/changed RFCs need re-embedding |
| OpenRouter rate limiting during indexing | Medium | Reuse `RateLimitRetryingChatClient` pattern (429 → backoff + retry); configurable concurrency |
| Some RFCs have non-standard formatting | Medium | Parser tested against diverse sample (early RFCs, modern RFCs, short, long) |
| pgvector extension not available in user's PostgreSQL | Low | Migration runner checks + clear error message; Docker Compose with pgvector-enabled image documented |
| Embedding dimension mismatch between model and schema | Medium | `RfcRagOptions.EmbeddingDimensions` configures both schema creation and runtime; validation at startup |
| ABNF detection false positives/negatives | Low | Test against known ABNF-heavy RFCs (5234, 7405, 8610); use conservative heuristics |

## Open Questions (Resolved)

1. **Embedding model**: **`text-embedding-3-small` (1536d)** via OpenRouter — already have an API key, OpenAI-compatible, 1536 dimensions provide better semantic resolution for technical text than 768d models. One-time indexing cost ~$0.10-0.50.
2. **ABNF storage**: **Separate `rfc_abnf_blocks` table** — dedicated GIN-indexed `tsvector` column for grammar-specific full-text search, foreign-keyed to `rfc_sections`. ABNF (Augmented Backus-Naur Form) is the formal grammar notation used in IETF RFCs to define protocol syntax precisely (message formats, handshake sequences, wire encodings).
3. **Normative keywords**: **Pre-extracted `normative_occurrences` table** — columns: `rfc_number`, `section_id`, `keyword` (enum), `line_offset`. B-tree index on `(keyword, rfc_number)` for sub-50ms queries.
4. **Indexing trigger**: **Auto-index (sync) on every MCP server start** — the server blocks until indexing completes, then starts listening. Incremental SHA256-based skip means subsequent starts are fast (seconds, not minutes). No separate CLI command needed. First run indexes everything (~10-15 min for ~9,800 RFCs).
