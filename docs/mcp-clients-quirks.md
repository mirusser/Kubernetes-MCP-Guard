# MCP Client Quirks

Known behavioural oddities in MCP clients when connecting to InfraGate.

---

## Claude Code CLI — Parent-project scope overrides repo OAuth config

### Problem

Claude Code CLI stores MCP server configs in `~/.claude.json` scoped by the directory you were in when you ran `claude mcp add`. When you later open a project that lives *inside* that directory, Claude Code inherits the parent-scoped config and uses it instead of the repo's `.mcp.json`.

If you previously added `infra-gate` with a manual `Authorization` header from your home directory, that config shadows the OAuth setup you configure in the repo later.

**Symptom — gateway log:**

```
Microsoft.IdentityModel.Tokens.SecurityTokenMalformedException: IDX14100: JWT is not well formed, there are no dots (.).
```

The gateway is receiving an old opaque bearer value (e.g. `change-me`) instead of the OAuth JWT. Opaque values have no `.` characters, so the JWT parser rejects them immediately before any signature check.

**Symptom — Claude Code:**

```
Got new credentials, but infra-gate rejected them on reconnect.
Try re-authenticating, or restart Claude Code if it persists.
```

Re-authenticating does not help because the stale config is read before the OAuth flow result is applied.

### Reproduction steps

1. While in `$HOME` (or any parent of your repo), add the MCP server with a manual Authorization header:
   ```bash
   claude mcp add infra-gate --transport http http://127.0.0.1:3001/mcp \
     --header "Authorization: Bearer change-me"
   ```
2. Switch to OAuth and put the OAuth config in the repo's `.mcp.json`.
3. Run `docker compose -f deploy/local-oauth/compose.yaml up --build`.
4. Open Claude Code inside the repo directory and use `/mcp`.
5. The gateway receives `Bearer change-me` and rejects it with the "no dots" error.

### Root cause

`~/.claude.json` stores per-project MCP configs under a `projects` key keyed by absolute path:

```json
{
  "projects": {
    "/home/youruser": {
      "mcpServers": {
        "infra-gate": {
          "type": "http",
          "url": "http://127.0.0.1:3001/mcp",
          "headers": { "Authorization": "Bearer change-me" }
        }
      }
    }
  }
}
```

Because the repo path (`/home/youruser/…/k8s-toolkit`) is a child of `/home/youruser`, Claude Code treats the home-directory entry as a parent-project config and inherits it. This takes precedence over the repo-level `.mcp.json`.

### Fix

Remove the stale entry from `~/.claude.json`. Run this while Claude Code is **not** running (it rewrites the file on exit):

```bash
python3 - <<'EOF'
import json, shutil, time, pathlib

path = pathlib.Path.home() / '.claude.json'
shutil.copy(path, f'{path}.bak.{int(time.time())}')

d = json.loads(path.read_text())
parent_mcp = d.get('projects', {}).get(str(pathlib.Path.home()), {}).get('mcpServers', {})
if parent_mcp.pop('infra-gate', None) is not None:
    print('Removed stale infra-gate entry')
else:
    print('Entry not found — nothing to remove')

path.write_text(json.dumps(d, indent=2))
EOF
```

Then restart Claude Code and use `/mcp` to trigger a fresh OAuth login.

---

## Claude Code CLI — Stale empty OAuth token blocks re-authentication

### Problem

Claude Code caches MCP OAuth state in `~/.claude/.credentials.json` under a `mcpOAuth` key. If a previous OAuth flow attempt failed partway through (e.g. Keycloak was unreachable at the time of token exchange), Claude Code may cache an entry with `"accessToken": ""`. On subsequent reconnects it sends this empty string as a Bearer token, which the gateway also rejects with the "no dots" error.

**Symptom:** Same "no dots" JWT error in the gateway log, and the "rejected on reconnect" message in Claude Code, even after a successful re-authentication attempt in the same session.

### Reproduction steps

1. Start the Keycloak local OAuth compose stack, but interrupt it before Keycloak is ready.
2. Trigger an MCP connection from Claude Code — the OAuth discovery partially completes but token exchange fails.
3. Restart the compose stack fully.
4. Claude Code now sends `Bearer ` (empty) from its cache → 401 "no dots".

### Fix

Clear the stale `infra-gate` entries from `~/.claude/.credentials.json`. Open the file and delete any keys under `mcpOAuth` whose name starts with `infra-gate` and whose `accessToken` value is empty or expired. Leave the `claudeAiOauth` block and `organizationUuid` intact.

After editing, restart Claude Code and use `/mcp` to go through a clean OAuth flow.

---

## All Clients — DPoP (Demonstrating Proof-of-Possession) Support

### Problem

InfraGate implements RFC 9449 (DPoP) to prevent JWT bearer token replay attacks. Internal services and the Approval UI are strictly required to use DPoP-bound tokens. However, most external MCP clients (including Claude Code and Cursor) do not yet support DPoP during their OAuth flow.

### Workaround / Behavior

To maintain compatibility with current MCP clients, the Keycloak `mcp-client` configuration **does not enforce** DPoP binding. Standard OAuth bearer tokens will be accepted from this client. This is a deliberate compromise documented as a residual security risk.

If an MCP client ever attempts to send a `DPoP` header but provides an invalid proof or signature, the Gateway *will* reject it (a malformed DPoP request is strictly rejected). But if the client simply uses standard Bearer authentication with no `DPoP` header, the Gateway will process it normally (provided the token is valid).
