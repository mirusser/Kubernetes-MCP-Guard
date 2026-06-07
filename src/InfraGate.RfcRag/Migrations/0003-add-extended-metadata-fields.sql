alter table rfc_rag.indexed_rfcs
    add column if not exists rfc_date text;

alter table rfc_rag.indexed_rfcs
    add column if not exists category text;

alter table rfc_rag.indexed_rfcs
    add column if not exists authors text[] not null default '{}';

alter table rfc_rag.indexed_rfcs
    add column if not exists issn text;
