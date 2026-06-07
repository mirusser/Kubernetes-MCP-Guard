# Implementation Plan: RFC RAG MCP Search Improvements

## Overview

Improve the `InfraGate.RfcRag` MCP server based on real friction observed during agent-driven RFC research sessions. The six pain points discovered are: (1) no section tree navigation, (2) no grammar-style metadata (ABNF vs TLS-presentation-lang vs CDDL), (3) parent sections return only prose with no way to fetch children in one call, (4) full-document retrieval is too large to use, (5) no cross-reference expansion for types referenced across sections, and (6) silent exception swallowing in search tools. Each maps to a concrete, testable change in the existing codebase.

The project is a standalone .NET 10 MCP server using PostgreSQL + pgvector with Dapper, ModelContextProtocol 1.3.0, and `Microsoft.Extensions.AI` for embedding generation. No internal dependencies on other InfraGate projects.

## Architecture Decisions

- **New migration files for schema changes** — follow the existing `0001-`/`0002-`/`0003-` naming pattern. No schema retrofits.
- **Grammar style is an enum-like text field** — one of `"abnf"`, `"tls-presentation-lang"`, `"cddl"`, `"asn.1"`, `"none"`. Simple text column with a CHECK constraint; no enum type in Postgres.
- **`get_rfc_toc` is a new MCP tool** — not a parameter on existing tools. It returns a flat `{ "section": "heading" }` map for the given RFC. No hierarchy nesting in the JSON response — the section identifiers already encode nesting (e.g., "4.4.2", "4.4.3").
- **`depth` and `expand` are optional parameters on `get_rfc_section`** — `depth=0` (default) returns one section; `depth=1` returns the parent plus all direct children. `expand=true` resolves inline type references within the section text (e.g., expanding `SignatureScheme` from its definition section).
- **`get_rfc` becomes a meta-tool** — returns TOC + metadata + first-N-sections preview instead of dumping the full concatenated text. The full text is still accessible via repeated `get_rfc_section` calls.
- **Exception swallowing is replaced with throw** — `catch (Exception) { return "[]"; }` in three tools becomes `catch (Exception) { logger.LogError(...); throw; }` to match the repo's code-standards rule: "Do not swallow exceptions silently."

## Task List

### Phase 1: Schema Foundation

#### Task 1: Add grammar_style column to indexed_rfcs

**Description:** Add a new migration `0004-add-grammar-style.sql` that adds a `grammar_style text` column to `rfc_rag.indexed_rfcs` with a CHECK constraint limiting values to the known set. Create a `0005-add-section-parent-ref.sql` for optional parent-child section relationships, but keep it simple — initially grammar_style is enough.

**Acceptance criteria:**
- [ ] Migration `0004-add-grammar-style.sql` applies cleanly on an existing `rfc_rag` schema
- [ ] Column `grammar_style` exists on `indexed_rfcs` with CHECK constraint `IN ('abnf', 'tls-presentation-lang', 'cddl', 'asn.1', 'none')`
- [ ] Existing rows get `grammar_style = 'none'` as default
- [ ] `RfcMetadata` model has a `GrammarStyle` property (string, defaults to `"none"`)

**Verification:**
- [ ] Unit test: `RfcMetadata_GrammarStyle_DefaultsToNone` — new record has default value
- [ ] Integration test: migration 0004 runs and column is queryable via `GetIndexedRfcMetadataAsync`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.RfcRag/Migrations/0004-add-grammar-style.sql` (new)
- `src/InfraGate.RfcRag/Models/RfcMetadata.cs`
- `src/InfraGate.RfcRag/Search/MetadataRepository.cs` (update query projection)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`
- `tests/InfraGate.RfcRag.Tests/IntegrationTests/RfcRagIntegrationTests.cs`

**Estimated scope:** S (3 files + migration)

---

#### Task 2: Update RfcParser to detect grammar style

**Description:** Add grammar-style detection logic to `RfcParser`. Scan parsed RFC sections for known grammar patterns. Heuristic: if >50% of section text matches ABNF rule definitions (`rulename = ...`), classify as `"abnf"`. If sections contain TLS struct/enum/select patterns (`struct {`, `select (`, `enum {`), classify as `"tls-presentation-lang"`. If CDDL patterns (`somegroup = {`), classify as `"cddl"`. Otherwise `"none"`. Store the result in `RfcDocument.Metadata.GrammarStyle`.

**Acceptance criteria:**
- [ ] RFC 8446 (TLS 1.3) is detected as `"tls-presentation-lang"`
- [ ] RFC 9110 (HTTP) is detected as `"abnf"`
- [ ] RFC 9052 (CBOR) is detected as `"cddl"`
- [ ] RFC 2119 (Key words) is detected as `"none"`
- [ ] `RfcDocument.Metadata.GrammarStyle` is populated after parsing
- [ ] `RfcIndexer` stores the grammar_style when upserting

**Verification:**
- [ ] Unit test: `RfcParser_DetectGrammarStyle_Tls13_ReturnsTlsPresentationLang`
- [ ] Unit test: `RfcParser_DetectGrammarStyle_Http_ReturnsAbnf`
- [ ] Unit test: `RfcParser_DetectGrammarStyle_Plain_ReturnsNone`
- [ ] Integration test: indexing RFC 8446 stores grammar_style in DB

**Dependencies:** Task 1 (schema column exists)

**Files likely touched:**
- `src/InfraGate.RfcRag/Parsing/RfcParser.cs` (add `DetectGrammarStyle` method)
- `src/InfraGate.RfcRag/Models/RfcDocument.cs` (populate GrammarStyle in Metadata)
- `src/InfraGate.RfcRag/Indexing/RfcIndexer.cs` (store grammar_style in upsert)
- `src/InfraGate.RfcRag/Indexing/IndexingRepository.cs` (update INSERT/UPDATE SQL)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcParserTests.cs`

**Estimated scope:** M (4 source files + tests)

---

### Checkpoint: Phase 1
- [ ] Migration 0004 applies cleanly
- [ ] Grammar style is detected, stored, and queryable
- [ ] All pre-existing unit tests pass
- [ ] All pre-existing integration tests pass

---

### Phase 2: New MCP Tools & Parameters

#### Task 3: Add `get_rfc_toc` MCP tool

**Description:** Add a new `[McpServerTool]` method `GetRfcToc` to `RfcRagTools`. It queries `rfc_rag.rfc_sections` for all sections of an RFC, returning a flat JSON object mapping `section -> heading`. No new DB queries needed — reuse `SearchRepository.GetRfcAsync()`. Add the corresponding `GetTocAsync` method to `ISearchService` and `SearchService`.

**Acceptance criteria:**
- [ ] `get_rfc_toc(rfcNumber=8446)` returns `{"4": "Handshake Protocol", "4.1": "Key Exchange Messages", "4.1.1": "Cryptographic Negotiation", ...}`
- [ ] Sections with no heading return `null` as value
- [ ] Tool fails gracefully with `{"error": "RFC N is not indexed."}` for unknown RFCs

**Verification:**
- [ ] Unit test: `GetRfcToc_WithSections_ReturnsOrderedMap`
- [ ] Unit test: `GetRfcToc_NoSections_ReturnsError`
- [ ] Manual: call against a known RFC

**Dependencies:** None (reads existing schema)

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (new method)
- `src/InfraGate.RfcRag/Search/ISearchService.cs` (add `GetTocAsync`)
- `src/InfraGate.RfcRag/Search/SearchService.cs` (delegate to repository)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`

**Estimated scope:** S (3 source files + tests)

---

#### Task 4: Expose grammar_style in `get_rfc_metadata`

**Description:** Include the `grammarStyle` field in the JSON response from `get_rfc_metadata`. The `RfcMetadata` model already maps from the indexed_rfcs row; the query in `MetadataRepository.GetIndexedRfcMetadataAsync` just needs to include the new column. CamelCase serialization will produce `"grammarStyle"`.

**Acceptance criteria:**
- [ ] `get_rfc_metadata(8446)` response includes `"grammarStyle": "tls-presentation-lang"`
- [ ] `get_rfc_metadata(9110)` response includes `"grammarStyle": "abnf"`

**Verification:**
- [ ] Unit test: `GetRfcMetadata_IncludesGrammarStyle`
- [ ] Manual: call against indexed RFCs

**Dependencies:** Task 2 (grammar style data exists in DB)

**Files likely touched:**
- `src/InfraGate.RfcRag/Search/MetadataRepository.cs` (add `grammar_style` to SELECT)
- `src/InfraGate.RfcRag/Models/RfcMetadata.cs` (already has GrammarStyle from Task 1)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`

**Estimated scope:** XS (1 source file + tests)

---

#### Task 5: Add `depth` parameter to `get_rfc_section`

**Description:** Extend `get_rfc_section` with an optional `depth` parameter (int, default 0). When `depth=0`, behavior is unchanged (returns exactly one section). When `depth=1`, returns the requested section plus all immediate child sections. Children are determined by prefix matching on the section identifier: e.g., section `"4.4"` with `depth=1` also returns `"4.4.1"`, `"4.4.2"`, `"4.4.3"`, `"4.4.4"`. Add a `GetSectionWithChildrenAsync` method to `SearchRepository` that does a `WHERE section LIKE @Prefix || '.%' AND rfc_number = @RfcNumber` query. The response wraps sections in a JSON object with a `"sections"` array.

**Acceptance criteria:**
- [ ] `get_rfc_section(rfcNumber=8446, section="4.4", depth=0)` returns only section 4.4
- [ ] `get_rfc_section(rfcNumber=8446, section="4.4", depth=1)` returns section 4.4 plus 4.4.1, 4.4.2, 4.4.3, 4.4.4
- [ ] `depth=1` on a leaf section returns just that section (no children match the prefix)
- [ ] Response format when depth>0: `{"section": {...}, "children": [{...}, ...]}`

**Verification:**
- [ ] Unit test: `GetRfcSection_Depth0_ReturnsSingleSection`
- [ ] Unit test: `GetRfcSection_Depth1_ReturnsSectionWithChildren`
- [ ] Unit test: `GetRfcSection_Depth1_Leaf_ReturnsSingleSection`
- [ ] Manual: call against RFC 8446 section 4.4

**Dependencies:** None (reads existing schema via LIKE query)

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (add `depth` param, branching logic)
- `src/InfraGate.RfcRag/Search/SearchRepository.cs` (add `GetSectionWithChildrenAsync`)
- `src/InfraGate.RfcRag/Search/ISearchService.cs` (add method)
- `src/InfraGate.RfcRag/Search/SearchService.cs` (delegate)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`

**Estimated scope:** S (4 source files + tests)

---

#### Task 6: Add `expand` parameter to `get_rfc_section`

**Description:** Add an optional `expand` boolean parameter (default false) to `get_rfc_section`. When true, the tool scans the section text for type references (capitalized identifiers that match other section headings or known grammar type names), fetches those referenced types' sections via a secondary query, and includes them in the response as `{"section": {...}, "expandedTypes": {"TypeName": {...section...}}}`. The type reference detection is heuristic: match PascalCase words that appear as section headings or as `enum { TYPE_NAME(value) }` / `struct { ... } TypeName` definitions elsewhere in the same RFC.

Keep scope narrow: only expand types that are:
1. Defined in the same RFC
2. Match a section heading or a known grammar production name (e.g., `SignatureScheme`, `HandshakeType`, `CipherSuite`)
3. Actually referenced in the current section's text

**Acceptance criteria:**
- [ ] `get_rfc_section(rfcNumber=8446, section="4.4.3", expand=true)` includes `"expandedTypes": {"SignatureScheme": {...section for 4.2.3...}}`
- [ ] `get_rfc_section(rfcNumber=8446, section="4.1.2", expand=true)` includes `"expandedTypes": {"ProtocolVersion": ..., "Random": ..., "CipherSuite": ..., "Extension": ...}` (or as many as are cross-referenced)
- [ ] `expand=false` (default) returns the section unchanged

**Verification:**
- [ ] Unit test: `GetRfcSection_Expand_IncludesReferencedTypes`
- [ ] Unit test: `GetRfcSection_Expand_NoReferences_ReturnsSectionAlone`
- [ ] Manual: call against TLS 1.3 sections with known cross-references

**Dependencies:** None (reads existing schema; type resolution is a client-side join in SearchService, not SQL)

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (add `expand` param)
- `src/InfraGate.RfcRag/Search/ISearchService.cs` (add expand interface)
- `src/InfraGate.RfcRag/Search/SearchService.cs` (add type-resolution logic)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs`

**Estimated scope:** M (3 source files + tests; type-resolution heuristics need careful testing)

---

### Checkpoint: Phase 2
- [ ] `get_rfc_toc` returns navigable section tree
- [ ] `get_rfc_metadata` includes grammarStyle
- [ ] `depth` parameter works for parent+children fetch
- [ ] `expand` parameter resolves cross-referenced types
- [ ] All pre-existing and new unit tests pass

---

### Phase 3: Search & Retrieval Fixes

#### Task 7: Replace `get_rfc` full-text dump with meta-tool

**Description:** The current `get_rfc` tool concatenates all section text into a single string, which produces output too large for agents to use (~693KB for RFC 8446). Replace it with a `get_rfc` that returns metadata + TOC + first N sections. The response shape becomes:

```json
{
  "rfcNumber": 8446,
  "title": "The Transport Layer Security (TLS) Protocol Version 1.3",
  "sourcePath": "rfc8446.txt",
  "url": "https://www.rfc-editor.org/rfc/rfc8446",
  "sectionCount": 72,
  "toc": {"4": "Handshake Protocol", "4.1": "Key Exchange Messages", ...},
  "sections": [ /* first 20 sections as preview */ ]
}
```

The `text` field is removed from the top-level response. Full section content is still available via `get_rfc_section`.

**Acceptance criteria:**
- [ ] `get_rfc(8446)` response is under 50KB (measured, not asserted)
- [ ] Response includes `toc` (section-number-to-heading map), `sections` (first 20 RfcSection objects), `sectionCount`
- [ ] Response no longer includes a concatenated `text` field
- [ ] Backward compatible: existing callers that consume `text` field will break — document this in the DRAFT.md notes

**Verification:**
- [ ] Unit test: `GetRfc_ReturnsTocAndPreviewSections`
- [ ] Unit test: `GetRfc_EmptyRfc_ReturnsError`
- [ ] Manual: verify response size is usable

**Dependencies:** Task 3 (toc building), but `get_rfc_toc` is a separate tool; this task can use `SearchRepository.GetRfcAsync` directly to build the TOC map.

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (rewrite `GetRfc`)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs` (update existing tests, add new)

**Estimated scope:** S (1 source file + tests)

---

#### Task 8: Fix silent exception swallowing in search tools

**Description:** Three tools in `RfcRagTools` swallow exceptions silently:
- `SearchRfc`: `catch (Exception) { return "[]"; }`
- `SearchNormative`: `catch (Exception) { return "[]"; }`
- `SearchAbnf`: `catch (Exception) { return "[]"; }`

Replace these with `catch (Exception ex) { logger.LogError(ex, "..."); throw; }` so failures propagate to the MCP client instead of silently returning empty results. The `RfcRagTools` class currently has no logger — inject `ILogger<RfcRagTools>` via the static method's context or move to instance methods. Preferred approach: add an `ILogger` parameter to each affected tool method, supplied by DI through the MCP framework's parameter injection (follow the existing pattern where `ISearchService search` is injected).

The `SearchService` also has similar silent catches in `SearchAsync`, `SearchNormativeAsync`, and `SearchAbnfAsync` — those log but still return `[]`. Change them to re-throw after logging as well, so the caller (the MCP tool) can decide the error response.

**Acceptance criteria:**
- [ ] `SearchRfc` propagates exceptions instead of returning `"[]"`
- [ ] `SearchNormative` propagates exceptions instead of returning `"[]"`
- [ ] `SearchAbnf` propagates exceptions instead of returning `"[]"`
- [ ] Exceptions are logged at Error level before propagation
- [ ] `SearchService.SearchAsync` re-throws after logging (instead of returning `[]`)
- [ ] `SearchService.SearchNormativeAsync` re-throws after logging
- [ ] `SearchService.SearchAbnfAsync` re-throws after logging

**Verification:**
- [ ] Unit test: `SearchRfc_WhenSearchThrows_PropagatesException`
- [ ] Unit test: `SearchNormative_WhenSearchThrows_PropagatesException`
- [ ] Unit test: `SearchAbnf_WhenSearchThrows_PropagatesException`
- [ ] Pre-existing "empty DB" tests still pass (those use `FakeSearchService`, not real exceptions)

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.RfcRag/Tools/RfcRagTools.cs` (remove try/catch or change to rethrow)
- `src/InfraGate.RfcRag/Search/SearchService.cs` (change catch blocks to rethrow)
- `tests/InfraGate.RfcRag.Tests/UnitTests/RfcRagToolsTests.cs` (add exception propagation tests, remove or update empty-DB tests that relied on silent catch)

**Estimated scope:** S (2 source files + tests)

---

### Checkpoint: Phase 3
- [ ] `get_rfc` returns TOC + preview, not massive text dump
- [ ] Exceptions propagate instead of silent return of `"[]"`
- [ ] All pre-existing and new unit tests pass
- [ ] Build succeeds

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Type-reference heuristic for `expand` is too loose (captures non-definition words) | Medium | Start with a conservative whitelist: only expand types that match `enum { NAME(value) }` or `struct { ... } NAME` patterns extracted from the same RFC. Can be tightened iteratively. |
| `depth` prefix matching on section strings matches unintended sections | Low | Section numbers in RFCs are well-structured. Test against edge cases like "1" vs "1.1" vs "10" (the prefix `"1."` will not match `"10."`). |
| Grammar style detection misclassifies mixed-language RFCs | Low | Some RFCs contain both ABNF and TLS presentation language (e.g., RFC 8446 has ABNF for protocol messages). Use the dominant style (>50% of grammar lines) to decide. |
| `get_rfc` response shape change breaks existing callers | Medium | The old `text` field is removed. Document in DRAFT.md. No known production callers beyond this agent session. |

## Open Questions

- **`expand` scope**: Should it resolve types across RFC boundaries (e.g., resolve `ExtensionType` from its full definition in Section 4.2)? Current plan limits to same-RFC only — cross-RFC expansion is a follow-up.
- **Grammar style for mixed RFCs**: If an RFC has both ABNF and TLS-presentation-lang sections (e.g., Section 3 uses TLS-PL, Section 4 uses ABNF), should grammar_style be per-section instead of per-RFC? Propose keeping per-RFC (majority vote) for v1.
- **`get_rfc` backward compat**: Should the old behavior be preserved as a separate tool `get_rfc_raw`? Keep scope small — if needed, add in a follow-up.
