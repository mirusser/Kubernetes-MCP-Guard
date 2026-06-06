alter table rfc_rag.indexed_rfcs
    add column if not exists updates int[] not null default '{}';

alter table rfc_rag.indexed_rfcs
    add column if not exists obsoletes int[] not null default '{}';
