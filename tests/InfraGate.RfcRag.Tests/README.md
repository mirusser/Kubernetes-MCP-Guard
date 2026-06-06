# InfraGate.RfcRag.Tests

Unit and integration tests for `InfraGate.RfcRag`.

**Covers:** RFC parser, embedding service, indexer, search service, MCP tools

## Structure

```
UnitTests/
  RfcParserTests.cs          # 5 tests: metadata, sections, normative keywords
IntegrationTests/
  RfcRagIntegrationTests.cs  # 5 tests: migrations, indexing, search, sections, incremental skip
  (requires Docker)
TestData/
  rfc2119.txt, rfc9110.txt, rfc8446.txt  # Real RFC fixtures
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
| Unit | 5 |
| Integration | 5 |
| **Total** | **10** |
