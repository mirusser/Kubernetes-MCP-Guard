# InfraGate.RfcRag.Tests

Unit and integration tests for `InfraGate.RfcRag`.

**Covers:** RFC parser, embedding service, indexer, search service, MCP tools

## Structure

```
UnitTests/
  RfcParserTests.cs          # 14 tests: metadata, sections, ABNF, normative keywords, TOC stripping
  RfcRagToolsTests.cs        # 19 tests: search, metadata, indexing, normative/ABNF search, error handling
IntegrationTests/
  RfcRagIntegrationTests.cs  # 6 tests: migrations, indexing, search, sections, metadata, incremental skip
  (requires Docker)
TestData/
  rfc2119.txt, rfc3986.txt, rfc8446.txt, rfc9000.txt, rfc9110.txt, rfc9999.txt, badfile.txt
```

## Running

```bash
# Unit tests (no dependencies — fast)
dotnet test tests/InfraGate.RfcRag.Tests/ --filter "Category!=Integration"

# Integration tests (requires Docker)
dotnet test tests/InfraGate.RfcRag.Tests/ --filter "Category=Integration"

# All tests
dotnet test tests/InfraGate.RfcRag.Tests/
```

| Category | Count |
|----------|-------|
| Unit | 33 |
| Integration | 6 |
| **Total** | **39** |
