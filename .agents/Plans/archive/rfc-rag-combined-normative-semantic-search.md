# Implementation Plan: Combined Normative + Semantic Search

## Overview

Add a `normative_keyword` filter parameter to `search_rfc` so agents can ask "find RFC sections about encryption where `MUST NOT` appears" in a single call. Currently this requires two separate passes — `search_rfc` (semantic) and `search_normative` (keyword) — and manual triangulation by the agent.

The implementation uses **Option A (semantic-first, normative post-filter, zero reindexing)**: run the existing semantic search to get top-K candidate sections, then cross-reference against the `normative_occurrences` table to keep only sections tagged with the requested keyword. This works because the normative occurrence table already links to sections via `(rfc_number, section)`, a natural join key that already exists in the schema.

The project is a standalone .NET 10 MCP server using PostgreSQL + pgvector with Dapper, ModelContextProtocol 1.3.0, and `Microsoft.Extensions.AI` for embedding generation.

## Architecture Decisions

- **`normative_keyword` is a new optional parameter on `search_rfc`** — not a separate tool. When omitted (default), behavior is unchanged. When provided (e.g., `"MUST NOT"`, `"SHOULD"`, `"REQUIRED"`), results are post-filtered to only sections containing that normative keyword.
- **Post-filter, not pre-filter** — semantic search runs first (casts a wide net), then results are cross-referenced against the normative occurrence store. This avoids the problem of `search_normative("MUST NOT")` returning 674K+ occurrences across all topics.
- **`limit` semantics change when `normative_keyword` is active** — the `limit` parameter controls the number of *returned* results after filtering. Internally, the semantic search fetches `limit * 3` candidates to account for filtering attrition. This prevents the tool from returning fewer results than requested when a restrictive keyword filters out most candidates.
- **The join key is `(rfc_number, section)`** — already present in both `rfc_sections` and `normative_occurrences`. No schema changes needed.
- **Keyword matching is exact** — the normative keyword string matches the `keyword` column in `normative_occurrences`. The table stores canonical forms (`"MUST"`, `"MUST NOT"`, `"SHOULD"`, `"SHOULD NOT"`, `"MAY"`, `"REQUIRED"`, `"OPTIONAL"`, `"SHALL"`, `"SHALL NOT"`, `"RECOMMENDED"`, `"NOT RECOMMENDED"`) as defined in the indexer.

## Task List

### Phase 1: Repository & Service Layer

#### Task 1: Add `FilterSectionsByNormativeKeywordAsync` to `SearchRepository`

**Description:** Add a Dapper query to `SearchRepository` that takes a list of `(rfc_number, section)` pairs and a `keyword` string, and returns only those pairs that have a matching entry in `rfc_rag.normative_occurrences`. The query should use PostgreSQL `UNNEST` with a composite type or a temporary table for efficient batch filtering — not N individual queries.

**Acceptance criteria:**
- [ ] Method signature: `Task<HashSet<(int, string)>> FilterSectionsByNormativeKeywordAsync(List<(int rfcNumber, string section)> candidates, string keyword, CancellationToken ct)`
- [ ] Returns only candidate tuples that exist in `normative_occurrences` with the given keyword
- [ ] Batch query (single round-trip to PostgreSQL) — not N individual SELECTs
- [ ] Empty candidates list returns empty HashSet without hitting the DB

**Verification:**
- [ ] Unit test: `FilterSectionsByNormativeKeyword_ExistingKeyword_ReturnsMatches` — feed 5 candidates, 2 have the keyword, returns those 2
- [ ] Unit test: `FilterSectionsByNormativeKeyword_NoMatches_ReturnsEmpty`
- [ ] Unit test: `FilterSectionsByNormativeKeyword_EmptyCandidates_ReturnsEmpty`
- [ ] Integration test: against real database with indexed RFCs

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.RfcRag/Search/SearchRepository.cs`
- `tests/InfraGate.RfcRag.Tests/UnitTests/SearchRepositoryTests.cs`
- `tests/InfraGate.RfcRag.Tests/IntegrationTests/RfcRagIntegrationTests.cs`

**Estimated scope:** S (1 source file + tests; one new Dapper method)

---

#### Task 2: Add `normative_keyword` support to `SearchService`

**Description:** Add an optional `string? normativeKeyword` parameter to `ISearchService.SearchAsync` and implement the post-filter pipeline in `SearchService`:
1. If `normativeKeyword` is null/empty → behave exactly as today (no changes)
2. If `normativeKeyword` is set → after semantic search returns candidates, call `SearchRepository.FilterSectionsByNormativeKeywordAsync` to filter to only sections with the keyword
3. Adjust the internal retrieval limit: when keyword filtering is active, fetch `limit * 3` candidates from the semantic search, then filter and trim to `limit` results
4. Results retain their original semantic ranking order

**Acceptance criteria:**
- [ ] `search_rfc(query="encryption", limit=10)` (no keyword) behaves identically to current behavior
- [ ] `search_rfc(query="encryption", limit=10, normative_keyword="MUST NOT")` returns at most 10 results, all containing "MUST NOT"
- [ ] When semantic search returns 30 candidates and only 3 have "MUST NOT", the tool returns 3 results (not 10, not 30)
- [ ] Ranking order is preserved (the semantically most relevant results among the filtered set come first)
- [ ] Unknown keyword returns empty results (not an error — the keyword simply has no matches)

**Verification:**
- [ ] Unit test: `SearchAsync_WithNormativeKeyword_FiltersResults`
- [ ] Unit test: `SearchAsync_WithoutNormativeKeyword_UnchangedBehavior`
- [ ] Unit test: `SearchAsync_NormativeKeywordNoMatches_ReturnsEmpty`
- [ ] Unit test: `SearchAsync_NormativeKeywordPreservesRanking` — verify first result is semantically closest among filtered set

**Dependencies:** Task 1 (repository method exists)

**Files likely touched:**
- `src/InfraGate.RfcRag/Search/ISearchService.cs` (add `normativeKeyword` param)
- `src/InfraGate.RfcRag/Search/SearchService.cs` (implement pipeline)
- `tests/InfraGate.RfcRag.Tests/UnitTests/SearchServiceTests.cs`

**Estimated scope:** S (2 source files + tests)

---

### Checkpoint: Phase 1
- [ ] Repository can filter candidate sections by normative keyword in one query
- [ ] SearchService pipeline integrates keyword filter
- [ ] All pre-existing unit tests pass (backward compatible)
- [ ] All pre-existing integration tests pass

---

### Phase 2: MCP Tool Surface

#### Task 3: Expose `normative_keyword` on `search_rfc` tool

**Description:** Add an optional `string? normativeKeyword = null` parameter to the `SearchRfc` method in `RfcRagTools`. Pass it through to `ISearchService.SearchAsync`. Add a description string on the parameter so MCP clients see it in tool metadata:

```
[Description("Optional normative keyword filter (e.g., 'MUST NOT', 'SHOULD', 'REQUIRED'). " +
             "When set, only sections containing this RFC 2119 keyword are returned.")]
```

No changes needed to the tool's return signature — it already returns JSON strings.

**Acceptance criteria:**
- [ ] `search_rfc` tool metadata includes `normative_keyword` as an optional parameter
- [ ] Passing `normative_keyword="MUST NOT"` filters results correctly (end-to-end)
- [ ] Omitting the parameter produces identical results to before
- [ ] Tool description is updated to mention the new parameter

**Verification:**
- [ ] Unit test: `SearchRfc_WithNormativeKeyword_PassesToService`
- [ ] Unit test: `SearchRfc_WithoutNormativeKeyword_OmitsParameter`
- [ ] Manual: call `search_rfc(query="unencrypted communication", normative_keyword="MUST NOT")` via MCP and verify filtered results

**Dependencies:** Task 2 (service method has the parameter)

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (add parameter to `SearchRfc`)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`

**Estimated scope:** XS (1 source file + tests)

---

### Checkpoint: Phase 2
- [ ] `search_rfc` accepts and uses `normative_keyword` parameter
- [ ] End-to-end: agent can use combined normative+semantic search in one call
- [ ] All pre-existing tests pass
- [ ] Manual verification with real RFC corpus

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `limit * 3` multiplier is too small for very restrictive keywords (e.g., searching a niche topic with `MUST NOT`) | Low | The multiplier is configurable. Start with 3x; if retrieval attrition is observed, bump to 5x in a follow-up. The `limit` parameter on the tool keeps the agent-visible count bounded regardless. |
| Batch filtering via PostgreSQL UNNEST on large candidate sets (300+ candidates at limit=100) | Low | Semantic search results are capped at a reasonable ceiling (suggest 200). PostgreSQL handles 200-row UNNEST trivially. |
| Keyword matching is case-sensitive vs. stored values | Low | Store keywords in canonical form during indexing; match exactly. The indexer already normalizes these. |
| `normative_keyword` parameter discovery — agents won't know it exists without reading docs | Medium | Include the parameter in the tool's JSON schema (ModelContextProtocol handles this via the `[Description]` attribute). Agents see it in tool metadata. |

## Open Questions

- **Should `search_normative` also get a `semantic_query` parameter (Option B)?** The reverse direction (normative-first, semantic re-rank) is useful when searching for rarer keywords like `"SHALL NOT"` where the full normative set is small. Defer to a follow-up plan — Option A alone covers the most common use case.
- **Should the `limit * N` multiplier be configurable?** For v1, hardcode 3x. If retrieval attrition becomes a problem, expose as a server-level configuration option in a follow-up.
- **Should filtered-out sections still appear in results with a `normative_keywords: []` marker?** No — keep it simple. The parameter means "only show sections with this keyword." Agents that want both filtered and unfiltered can make two calls.

## Post-Implementation Validation

After Tasks 1-3 are complete, verify with the exact query that motivated this feature:

```
search_rfc(query="must not use unencrypted communication cleartext plaintext prohibited",
           normative_keyword="MUST NOT",
           limit=10)
```

Expected: results should include RFC 9325 §3.2 (Strict TLS), RFC 4880 §13.4 (OpenPGP plaintext prohibition), and other RFCs that explicitly use `MUST NOT` in the context of encryption/cleartext requirements — all in a single call, without the agent needing to run `search_normative` separately and manually cross-reference.
