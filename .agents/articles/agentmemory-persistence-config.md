# agentmemory Persistence Configuration

## Problem

`agentmemory` stores all memory data (sessions, observations, memories, search
indexes) in a file-based KV store via the iii-engine. The bundled
`iii-config.yaml` uses a **relative path** for the state store:

```yaml
file_path: ./data/state_store.db
```

This means the data directory depends on `process.cwd()` at startup. Starting
agentmemory from different directories creates separate, isolated databases.

## Solution

Custom config at `~/.agentmemory/iii-config.yaml` with absolute paths:

```yaml
file_path: /home/mirusser/.agentmemory/data/state_store.db
```

### Persistence layers

| Layer | What it persists | Reliability |
|-------|-----------------|-------------|
| `iii-engine` KV store | Sessions, observations, memories, graph nodes, metadata | SQLite-backed, survives restart |
| `IndexPersistence` | BM25 + vector search indexes | Debounced auto-save (5s), loaded on boot |
| Git snapshots | Full state dump + versioned commits | Manual via `mem::snapshot-create` / `memory_snapshot_create` MCP tool |

### Startup

Two options:

1. **Use the shell function** (recommended):
   ```bash
   agentmem   # starts engine + agentmemory
   ```
   Defined in `~/.bashrc`. Splits into two steps:
   - `iii --config ~/.agentmemory/iii-config.yaml &` (background)
   - `agentmemory --no-engine` (foreground, Ctrl+C to stop)

2. **Hide the bundled config** (so bare `agentmemory` picks up the custom one):
   ```bash
   sudo mv /usr/lib/node_modules/@agentmemory/agentmemory/dist/iii-config.yaml{,.bak}
   ```
   Then `agentmemory` from any directory works. Re-run after `npm upgrade -g`.

### Data layout

```
~/.agentmemory/
├── iii-config.yaml          # custom config with absolute paths
├── data/
│   ├── state_store.db/      # KV store (sessions, obs, memories, indexes)
│   │   ├── mem%3Asessions.bin
│   │   ├── mem%3Aobs%3A*.bin
│   │   ├── mem%3Amemories.bin
│   │   ├── mem%3Aindex%3Abm25.bin
│   │   └── ...
│   └── stream_store/        # stream data
├── backups/                 # agent config backups
├── .env                     # env overrides (LLM keys, etc.)
└── preferences.json
```
