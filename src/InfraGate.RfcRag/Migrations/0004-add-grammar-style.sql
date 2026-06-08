alter table rfc_rag.indexed_rfcs
    add column if not exists grammar_style text not null default 'none';

alter table rfc_rag.indexed_rfcs
    add constraint ck_grammar_style
    check (grammar_style in ('abnf', 'tls-presentation-lang', 'cddl', 'asn.1', 'none'));
