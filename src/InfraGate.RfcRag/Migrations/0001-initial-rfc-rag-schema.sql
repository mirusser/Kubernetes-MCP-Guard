create schema if not exists rfc_rag;

create table rfc_rag.schema_migrations
(
    filename text primary key,
    checksum_sha256 text not null,
    applied_at_utc timestamptz not null default now()
);

create extension if not exists vector;

create table rfc_rag.rfc_sections
(
    id              uuid primary key default gen_random_uuid(),
    rfc_number      int not null,
    title           text not null,
    section         text not null,
    heading         text,
    text            text not null,
    source_path     text not null,
    url             text not null,
    source_sha256   text not null,
    embedding       vector(1536),
    search_vector   tsvector generated always as (to_tsvector('english', text)) stored
);

create index ix_rfc_sections_rfc_number
    on rfc_rag.rfc_sections (rfc_number);

create index ix_rfc_sections_section
    on rfc_rag.rfc_sections (rfc_number, section);

create index ix_rfc_sections_search
    on rfc_rag.rfc_sections using gin (search_vector);

create index ix_rfc_sections_embedding_hnsw
    on rfc_rag.rfc_sections using hnsw (embedding vector_cosine_ops)
    with (m = 16, ef_construction = 64);

-- Track indexed RFCs for incremental re-index (SHA256-based skip)
create table rfc_rag.indexed_rfcs
(
    rfc_number      int primary key,
    source_path     text not null,
    source_sha256   text not null,
    title           text not null,
    section_count   int not null,
    indexed_at_utc  timestamptz not null default now()
);

-- ABNF grammar blocks extracted from RFC sections
create table rfc_rag.rfc_abnf_blocks
(
    id              uuid primary key default gen_random_uuid(),
    section_id      uuid not null references rfc_rag.rfc_sections(id) on delete cascade,
    rfc_number      int not null,
    section         text not null,
    abnf_text       text not null,
    rule_names      text[] not null default '{}',
    search_vector   tsvector generated always as (to_tsvector('english', abnf_text)) stored
);

create index ix_rfc_abnf_blocks_section_id
    on rfc_rag.rfc_abnf_blocks (section_id);

create index ix_rfc_abnf_blocks_rfc_number
    on rfc_rag.rfc_abnf_blocks (rfc_number);

create index ix_rfc_abnf_blocks_search
    on rfc_rag.rfc_abnf_blocks using gin (search_vector);

create index ix_rfc_abnf_blocks_rule_names
    on rfc_rag.rfc_abnf_blocks using gin (rule_names);

-- Normative keyword occurrences (RFC 2119/8174)
create table rfc_rag.normative_occurrences
(
    id              uuid primary key default gen_random_uuid(),
    section_id      uuid not null references rfc_rag.rfc_sections(id) on delete cascade,
    rfc_number      int not null,
    keyword         text not null,
    line_offset     int not null
);

create index ix_normative_occurrences_keyword_rfc
    on rfc_rag.normative_occurrences (keyword, rfc_number);

create index ix_normative_occurrences_section_id
    on rfc_rag.normative_occurrences (section_id);
