# InfraGate.Approvals.Postgres

`InfraGate.Approvals.Postgres` owns durable PostgreSQL persistence for the generic approval workflow. It implements `IApprovalPersistence` via Npgsql, runs schema migrations on startup, and provides `PostgresApprovalAccessCodeStore` for one-time Approval Access Codes.

**Owns:** durable persistence for approval workflows
