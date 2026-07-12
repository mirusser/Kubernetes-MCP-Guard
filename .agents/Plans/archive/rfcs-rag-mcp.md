Yes, the idea is worth it. **But I would not start by running Docling over the RFC TXT corpus.** For RFCs, the source is already clean, canonical plain text, so Docling’s biggest strengths—PDF layout, OCR, tables, figures, reading order—do not buy you much for the `in-notes/*.txt` files. Docling does support plain text, Markdown, integrations, chunking, and an MCP server, but its value here is more as optional pipeline glue than as the core parser. ([GitHub][1])

The better design is:

```text
rsync RFC TXT mirror
        ↓
RFC-aware parser / cleaner
        ↓
section-level chunks + metadata
        ↓
hybrid index: exact lookup + BM25/FTS + vector search
        ↓
local read-only MCP server for coding agents
```

The RFC Editor already provides exactly the TXT corpus via `rfcs-text-only`, and also provides JSON-only modules if you later want structured metadata alongside the text. ([RFC Editor][2])

## My recommendation

Use **custom RFC-aware RAG**, not generic document conversion.

RFCs have predictable structure:

```text
RFC 9110              HTTP Semantics             June 2022

1.  Introduction
1.1.  Purpose
...
2119 keywords
ABNF blocks
IANA Considerations
Security Considerations
References
```

That means you can get much better agent behavior by parsing:

```text
rfc_number
title
date
status
obsoletes / updates
section number
section heading
line offsets
normative keywords
ABNF/code blocks
references
```

A generic converter may flatten or alter structure that is actually important for citation fidelity.

## Where Docling could still help

Use Docling only if you also want to ingest **PDFs, HTML, drafts, books, vendor docs, academic papers, or non-RFC specs** into the same pipeline. It supports many formats, a unified `DoclingDocument`, Markdown/JSON export, LangChain/LlamaIndex/Haystack integrations, and token-aware chunking. ([Docling Project][3])

Docling’s chunking may be useful if you want a ready-made chunker. Its hybrid chunker is tokenization-aware and designed for document-based hierarchical chunking, while its line-based chunker preserves line boundaries, which matters for structured content like code, logs, and tables. ([Docling Project][4]) ([Docling Project][5])

But for RFCs specifically, I would first write a small RFC parser.

## MCP is a very good fit

MCP is designed to let applications expose **resources, prompts, and tools** to agents. The current spec describes servers as providers of context and capabilities, including resources and tools. ([Model Context Protocol][6])

For coding agents, I would expose tools like:

```text
search_rfc(query, limit=10)
get_rfc(rfc_number)
get_rfc_section(rfc_number, section="6.3")
search_normative(term, rfcs=[...])
search_abnf(query, rfcs=[...])
find_updates_obsoletes(rfc_number)
```

And resources like:

```text
rfc://9110
rfc://9110/section/6.3
rfc://8446/section/4.1.2
```

That lets agents cite precise sections instead of vaguely saying “according to the RFC.”

## Do hybrid retrieval, not vector-only

For RFCs, **exact wording matters**. “MUST”, “MUST NOT”, “SHOULD”, “connection”, “stream”, “frame”, “nonce”, “canonicalization” are often semantically overloaded. A vector search alone can retrieve plausible but wrong sections.

Use three retrieval paths:

```text
1. Exact lookup
   RFC number, section number, title, keyword.

2. Lexical search
   SQLite FTS5, Tantivy, ripgrep, Meilisearch, OpenSearch, etc.

3. Vector search
   Useful for conceptual questions like “how does HTTP content negotiation work?”
```

Then merge/rerank results and return small snippets with section metadata.

A very practical local stack:

```text
SQLite + FTS5          lexical index
sqlite-vec / Qdrant    vector index
Python MCP server      agent interface
sentence-transformers  local embeddings
ripgrep fallback       debugging / exact grep
```

On Arch:

```bash
sudo pacman -S python python-pip uv sqlite ripgrep
```

Then keep your RFC mirror separate from your index:

```text
~/rfc-mirror/          raw rsync mirror
~/rfc-rag/index.db     metadata + FTS
~/rfc-rag/vectors/     vector store if separate
```

## Chunking strategy I’d use

Do **section-based chunks**, not fixed 512-token chunks.

Suggested chunk shape:

```json
{
  "id": "rfc9110:6.3",
  "rfc": 9110,
  "title": "HTTP Semantics",
  "section": "6.3",
  "heading": "Header Fields",
  "text": "...",
  "source_path": "in-notes/rfc9110.txt",
  "url": "https://www.rfc-editor.org/rfc/rfc9110.txt"
}
```

For large sections, split further:

```text
rfc9110:6.3#p1
rfc9110:6.3#p2
```

But always preserve the parent section heading. This is exactly the sort of contextualization Docling’s chunking examples emphasize, but you can implement it more precisely for RFCs. ([Docling Project][4])

## Minimal build path

Start with this. Skip Docling at first.

```bash
mkdir -p ~/rfc-rag
cd ~/rfc-rag

uv init
uv add mcp sentence-transformers qdrant-client sqlite-utils
```

Build an indexer that:

```text
1. walks ~/rfc-mirror/in-notes/**/*.txt
2. detects RFC number from filename
3. removes page headers/footers and form feeds
4. extracts title/date from the front matter
5. splits on section headings
6. stores chunks in SQLite FTS5
7. optionally embeds each chunk
```

Then write an MCP server that returns results like:

```text
RFC 9110 §6.3 Header Fields
Source: ~/rfc-mirror/in-notes/rfc9110.txt
Excerpt:
...
```

## When I’d add Docling

Add Docling later if one of these becomes true:

```text
You want to ingest PDFs or non-RFC specs.
You want one unified pipeline for many document formats.
You want Docling’s LangChain/LlamaIndex/Haystack integrations.
You want its ready-made chunking/export formats.
You want to expose document conversion itself over MCP.
```

Docling already has an MCP server for document processing experiments, but that is different from a purpose-built RFC retrieval MCP server. ([Docling Project][7])

## Verdict

**Worth building? Yes.**

**Worth using Docling on RFC TXT as the main preprocessing step? Probably no.**

I’d build a small RFC-native indexer and expose it over MCP. Use Docling as an optional sidecar for non-RFC documents or if you later decide you want its `DoclingDocument` ecosystem. For coding agents, the highest-value feature will be **precise RFC-number + section retrieval with exact snippets**, not generic document conversion.

[1]: https://github.com/docling-project/docling "GitHub - docling-project/docling: Get your documents ready for gen AI · GitHub"
[2]: https://www.rfc-editor.org/series/rfc-download/ "Download RFCs | RFC Editor"
[3]: https://docling-project.github.io/docling/ "Index - Docling"
[4]: https://docling-project.github.io/docling/_generated/examples/hybrid_chunking/ "Hybrid chunking - Docling"
[5]: https://docling-project.github.io/docling/_generated/examples/line_based_chunking/ "Line-Based Token Chunking - Docling"
[6]: https://modelcontextprotocol.io/specification/2025-11-25 "Specification - Model Context Protocol"
[7]: https://docling-project.github.io/docling/usage/mcp/ "Mcp - Docling"
