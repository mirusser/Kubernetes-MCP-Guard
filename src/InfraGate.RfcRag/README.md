# InfraGate.RfcRag

Local RAG (Retrieval-Augmented Generation) MCP server for RFCs. Indexes a local RFC mirror
into PostgreSQL with pgvector for vector search and PostgreSQL full-text search for lexical
retrieval, then exposes MCP tools for coding agents to search and cite RFCs with section-level
precision.

**Owns:** RFC parsing, section-based chunking, hybrid search (vector + lexical + exact),
ABNF grammar extraction, normative keyword indexing, MCP tool exposure

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL with [pgvector](https://github.com/pgvector/pgvector) extension
- OpenRouter API key (for embedding generation)
- Local RFC mirror (rsync from `rsync.rfc-editor.org`)

## Quick Start

```bash
# 1. Set up RFC mirror (one-time)
rsync -avz --delete rsync.rfc-editor.org::rfcs-text-only ~/OtherRepos/rfc-mirror/

# 2. Set environment variables
export InfraGate__RfcRag__PostgresConnectionString="Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres"
export InfraGate__RfcRag__RfcMirrorPath="$HOME/OtherRepos/rfc-mirror"
export InfraGate__OpenRouter__ApiKey="sk-or-..."

# 3. Enable pgvector in PostgreSQL (one-time)
psql "$InfraGate__RfcRag__PostgresConnectionString" -c "CREATE EXTENSION IF NOT EXISTS vector;"

# 4. Build and run (auto-indexes on first start)
dotnet run --project src/InfraGate.RfcRag/
```

On first run, the server indexes all ~9,800 RFCs (~10-15 minutes). Subsequent starts use
incremental SHA256-based skip detection and complete in seconds.

## Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `InfraGate__RfcRag__RfcMirrorPath` | `~/OtherRepos/rfc-mirror/` | Path to local RFC mirror |
| `InfraGate__RfcRag__PostgresConnectionString` | (required) | PostgreSQL connection string |
| `InfraGate__RfcRag__EmbeddingModel` | `openai/text-embedding-3-small` | OpenRouter embedding model |
| `InfraGate__RfcRag__EmbeddingBatchSize` | `20` | Batch size for embedding API calls |
| `InfraGate__RfcRag__OpenRouterEmbeddingEndpoint` | `https://openrouter.ai/api/v1` | OpenRouter API base URL for embedding requests |
| `InfraGate__RfcRag__RunMigrationsOnStartup` | `true` | Auto-apply SQL schema migrations |
| `InfraGate__OpenRouter__ApiKey` | (required) | OpenRouter API key |

## MCP Tools

### `search_rfc`
Hybrid search combining vector similarity and full-text lexical search with reciprocal rank fusion.
Returns ranked sections with excerpts.

```
Parameters: query (string), limit (int, default=10)
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, sourcePath, url, score }
```

### `get_rfc`
Retrieve the full text of an RFC by its number.

```
Parameters: rfcNumber (int)
Returns: JSON object with { rfcNumber, title, sourcePath, url, text, sections } (where sections is a JSON array of RfcSection objects)
```

### `get_rfc_section`
Retrieve a specific section of an RFC.

```
Parameters: rfcNumber (int), section (string, e.g. "6.3", "4.1.2")
Returns: JSON object with { id, rfcNumber, title, section, heading, text, sourcePath, url }
```

### `search_normative`
Search for normative keywords (RFC 2119/8174) across indexed RFCs.

```
Parameters: keyword (string), rfcNumbers (int[]?, optional), limit (int, default=20)
Valid keywords: MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, score }
```

### `search_abnf`
Search ABNF grammar definitions by rule name or fragment.

```
Parameters: query (string), rfcNumbers (int[]?, optional), limit (int, default=20)
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, score }
```

### `find_updates_obsoletes`
Find RFCs that update or obsolete a given RFC.

```
Parameters: rfcNumber (int)
Returns: { rfcNumber, title, updates: [...], obsoletes: [...], updated_by: [{number, title}], obsoleted_by: [{number, title}] }
```

### `rfc_stats`
Get statistics about the indexed RFC corpus.

```
Parameters: none
Returns: JSON string { indexedRfcs, sections, abnfBlocks, normativeOccurrences, lastIndexedAtUtc }
```

## Example Queries

```
"What does RFC 9110 say about content negotiation?"
→ search_rfc("content negotiation", limit=5)
→ get_rfc_section(9110, "8.6")

"Find all TLS 1.3 handshake ABNF"
→ search_abnf("handshake", rfcNumbers=[8446])

"Which RFCs MUST NOT allow unencrypted communication?"
→ search_normative("MUST NOT", limit=10)

"What obsoletes RFC 7230?"
→ find_updates_obsoletes(7230)
```

## Connecting Claude Code

```bash
claude mcp add-json --scope user rfc-rag \
  '{"type":"stdio","command":"dotnet","args":["run","--project","src/InfraGate.RfcRag/"]}'

claude
/mcp
```

## Connecting Codex

Add to `~/.codex/config.toml`:

```toml
[mcp_servers.rfc-rag]
command = "dotnet"
args = ["run", "--project", "src/InfraGate.RfcRag/"]
```

## Architecture

```
~/OtherRepos/rfc-mirror/*.txt
         │
         ▼
   RfcParser (section splitter, metadata, ABNF, normative keywords)
         │
         ├──► PostgreSQL + pgvector (vector search, cosine distance)
         ├──► PostgreSQL tsvector (full-text lexical search)
         └──► Hybrid retrieval with reciprocal rank fusion
         │
         ▼
   MCP stdio server (ModelContextProtocol 1.3.0)
         │
         ▼
   Coding agents (Claude Code, Codex)
```

## Database Schema

```
rfc_rag.rfc_sections          — primary search unit (vectors + FTS)
rfc_rag.indexed_rfcs           — SHA256 tracking for incremental indexing
rfc_rag.rfc_abnf_blocks        — extracted ABNF grammar blocks
rfc_rag.normative_occurrences  — pre-extracted normative keywords
rfc_rag.schema_migrations      — applied migration tracking
```

## Running Tests

```bash
# Unit tests (no dependencies)
dotnet test tests/InfraGate.RfcRag.Tests/ --filter "Category!=Integration"

# Integration tests (requires Docker)
dotnet test tests/InfraGate.RfcRag.Tests/ --filter "Category=Integration"
```

## Contents

- `RfcParser.cs`: parses raw RFC `.txt` files, strips page headers/footers, extracts metadata, splits into sections, detects ABNF blocks, extracts normative keywords.
- `RfcIndexer.cs`: walks the RFC mirror, parses each RFC, generates embeddings via OpenRouter, stores in PostgreSQL.
- `RfcRepository.cs`: Dapper-based data access for RFC sections, ABNF blocks, normative occurrences.
- `SearchService.cs`: hybrid search combining vector similarity, full-text lexical search, and exact section lookup.
- `RfcRagTools.cs`: MCP tool definitions exposed to coding agents.
- `Program.cs`: stdio MCP server with auto-indexing on startup.
- `ServiceCollectionExtensions.cs`: dependency injection registration for RFC RAG services and adapters.
- `RfcRagMigrationRunner.cs`: applies database schema migrations for the RFC RAG tables.
- `RfcDocument.cs`: represents a parsed RFC document with metadata and sections.
- `Settings/RfcRagOptions.cs`: configuration options for the RFC RAG pipeline.
- `RfcRagConventions.cs`: database schema and table name conventions.
- `EmbeddingService.cs`: generates text embeddings in batches using the configured generator.
- `ISearchService.cs`: interface for RFC search and retrieval operations.
- `IIndexerService.cs`: interface for the RFC indexing service.
- `SearchResult.cs`: represents a ranked search result from hybrid search.
- `Models/`: database entity models including `RfcSection.cs`, `RfcAbnfBlock.cs`, `NormativeOccurrence.cs`, and `RfcMetadata.cs`.

## Boundaries

This project depends on:
- PostgreSQL with the `pgvector` extension for vector storage and full-text search
- OpenRouter API for embedding generation (`text-embedding-3-small`)
- `ModelContextProtocol` SDK for MCP server transport
- `Microsoft.Extensions.AI` for embedding abstraction
- A local RFC mirror at the configured path (default: `~/OtherRepos/rfc-mirror/`)

This project has no dependencies on other InfraGate projects. It is a standalone tool.
