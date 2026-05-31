# agentmemory — Local Setup & Usage

## Overview

[agentmemory](https://github.com/rohitg00/agentmemory) is a persistent memory store
for AI coding agents. It uses a **iii-engine** KV store (SQLite-backed) and exposes a
REST API (port 3111) and a knowledge-graph viewer (port 3113).

## Architecture

```
agentmem()           # shell function in ~/.bashrc
 ├── iii --config ~/.agentmemory/iii-config.yaml &   # engine (background)
 └── agentmemory --no-engine                         # worker (foreground)
```

- **Engine** (`iii`): REST API on `:3111`, WebSocket on `:49134`
- **Worker** (`agentmemory --no-engine`): MCP interface + viewer on `:3113`
- **Viewer**: `http://localhost:3113` — knowledge graph UI (pan/zoom)

## Configuration

Custom config at `~/.agentmemory/iii-config.yaml`:

```yaml
workers:
  - name: iii-http
    config:
      port: 3111
      host: 127.0.0.1
  - name: iii-state
    config:
      adapter:
        name: kv
        config:
          store_method: file_based
          file_path: /home/mirusser/.agentmemory/data/state_store.db  # absolute path
```

The absolute path avoids cwd-dependent DB isolation (default config uses `./data/state_store.db` which
changes per working directory).

Auth: `AGENTMEMORY_SECRET=your-secret-here` in `~/.agentmemory/.env`.

## Running

```bash
agentmem       # start engine + worker
agentmem-stop  # stop both (kills iii + port 3111)
```

The `agentmem-stop` function runs:
```bash
kill $(pgrep -f 'iii.*iii-config') 2>/dev/null
kill $(lsof -t -i:3111) 2>/dev/null
```

## opencode MCP Integration

The project's opencode config at `~/.config/opencode/opencode.json` launches the agentmemory
MCP shim as a local server. **Both** `AGENTMEMORY_URL` **and** `AGENTMEMORY_SECRET` must be set:

```json
"agentmemory": {
  "type": "local",
  "command": ["npx", "-y", "@agentmemory/mcp"],
  "enabled": true,
  "environment": {
    "AGENTMEMORY_URL": "http://localhost:3111",
    "AGENTMEMORY_SECRET": "your-secret-here"
  }
}
```

> **Note**: The field is `"environment"`, not `"env"`. The schema `McpLocalConfig` only recognizes
> `"environment"` — using `"env"` silently drops the variables.

### Why both env vars are required

The MCP shim (`@agentmemory/mcp`) probes the engine at `/agentmemory/livez` — that endpoint
returns 200 even without auth, so the shim enters **proxy mode**. But the actual data endpoints
(`/agentmemory/memories`, `/agentmemory/mcp/call`, etc.) return **401 Unauthorized** without the
bearer token. Without `AGENTMEMORY_SECRET`, every memory save, recall, or search silently fails
and the shim returns an MCP error to the agent.

Without the secret, the only way data reaches the engine is through the REST API directly
(using the `Authorization` header) — MCP tools are broken.

### Startup order

The engine must be running **before** opencode starts its MCP shim. The recommended flow:

1. Run `agentmem` in a terminal (starts engine + worker)
2. Verify engine is up: `curl -s http://localhost:3111/agentmemory/livez`
3. Then start opencode (which launches the MCP shim that proxies to the engine)

If opencode starts before the engine, the MCP shim falls back to local InMemoryKV
(`standalone.json`) — data saved during that window won't appear in the viewer.

## Verification

After starting:

```bash
# Check ports
ss -tlnp | grep -E "3111|3113"

# Check REST API health
curl -s -H "Authorization: Bearer your-secret-here" \
  http://127.0.0.1:3111/agentmemory/health | python3 -m json.tool

# Check memories
curl -s -H "Authorization: Bearer your-secret-here" \
  http://127.0.0.1:3111/agentmemory/memories | python3 -m json.tool

# Open viewer
xdg-open http://localhost:3113
```

Expected:
- Port 3111: `iii` engine process
- Port 3113: `node /usr/bin/agentmemory` worker process
- Viewer at `:3113` returns HTTP 200

## Saving a Memory

Use the REST API (replace `memory_save` arguments as needed):

```bash
curl -s -H "Authorization: Bearer your-secret-here" \
  -X POST -H "Content-Type: application/json" \
  -d '{
    "name": "memory_save",
    "arguments": {
      "content": "your note here",
      "type": "fact",
      "concepts": "tag1,tag2",
      "files": "relevant/file/path"
    }
  }' \
  http://127.0.0.1:3111/agentmemory/mcp/call
```

**Type restriction**: The engine only accepts `pattern`, `preference`, `architecture`, `bug`,
`workflow`, or `fact`. Anything else is silently mapped to `fact`.

## Data Locations

| Data | Path |
|------|------|
| Engine KV store | `~/.agentmemory/data/state_store.db/` |
| Fallback (no engine) | `~/.agentmemory/standalone.json` |
| Config | `~/.agentmemory/iii-config.yaml` |
| Env vars | `~/.agentmemory/.env` |

## Syncing Data Between Standalone.json and Engine

The MCP tools proxy to the engine by default. If the engine is unreachable, they fall back
to an InMemoryKV backed by `~/.agentmemory/standalone.json`. Data written in fallback mode
is invisible to the viewer until synced.

### How to tell which mode is active

Run `agentmemory status` or check `~/.agentmemory/standalone.json` for data.
If the engine is reachable, the MCP shim prints:
```
[@agentmemory/mcp] proxying to agentmemory server at http://localhost:3111
```
If unreachable:
```
[@agentmemory/mcp] no server reachable at http://localhost:3111; falling back to local InMemoryKV
```

### Sync standalone.json → Engine

Stop the engine first (to avoid conflicts), then read each memory from standalone.json
and POST it through the engine's REST API:

```bash
python3 << 'PYEOF'
import json, urllib.request

with open('/home/mirusser/.agentmemory/standalone.json') as f:
    data = json.load(f)

memories = data.get('mem:memories', {})
api_url = "http://127.0.0.1:3111/agentmemory/mcp/call"
headers = {
    "Authorization": "Bearer your-secret-here",
    "Content-Type": "application/json"
}

saved = 0
for k, v in memories.items():
    # Engine only accepts: pattern, preference, architecture, bug, workflow, fact
    mtype = v.get("type", "fact")
    if mtype not in ("pattern","preference","architecture","bug","workflow","fact"):
        mtype = "fact"

    payload = {
        "name": "memory_save",
        "arguments": {
            "content": v["content"],
            "type": mtype,
            "concepts": ",".join(v.get("concepts", [])),
            "files": ",".join(v.get("files", []))
        }
    }
    req = urllib.request.Request(
        api_url,
        data=json.dumps(payload).encode(),
        headers=headers,
        method="POST"
    )
    urllib.request.urlopen(req)
    saved += 1

print(f"Synced {saved}/{len(memories)} memories to engine")
PYEOF
```

After syncing, clear standalone.json so future fallback writes start fresh:
```bash
echo '{}' > ~/.agentmemory/standalone.json
```

### Sync Engine → standalone.json (export)

The engine does not write to standalone.json automatically. To export engine data:

```bash
curl -s -H "Authorization: Bearer your-secret-here" \
  http://127.0.0.1:3111/agentmemory/memories \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(json.dumps({'mem:memories':{m['id']:m for m in d['memories']}}, indent=2))" \
  > ~/.agentmemory/standalone.json
```

### Prevention

To avoid split-brain scenarios, always start agentmemory with `agentmem` (which starts
both engine and worker together) rather than running `agentmemory --no-engine` standalone.

## Common Pitfalls

- **Duplicate instances**: Multiple `agentmemory --no-engine` or MCP shim processes can
  accumulate. Use `agentmem-stop` to kill all, then start fresh.
- **Viewer empty**: The viewer queries the engine's KV store. Run the sync script above
  if data was saved while the engine was down.
- **Type `lesson` not supported**: Engine only accepts `pattern`, `preference`, `architecture`,
  `bug`, `workflow`, or `fact`. Anything else maps silently to `fact`.
