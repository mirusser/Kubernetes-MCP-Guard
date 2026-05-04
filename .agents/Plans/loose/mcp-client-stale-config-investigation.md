Claude Code CLI inherits MCP server configs from parent-directory project scopes stored in `~/.claude.json`. When a server is added with a static bearer token (Mode B) from a parent directory (e.g. `$HOME`), subdirectory projects inherit it and it silently overrides the OAuth config in the repo's `.mcp.json`.

Worth investigating:
- Whether Claude Code exposes a first-class way to remove or scope-limit a project-level MCP entry (e.g. `claude mcp remove --scope project`) without manually editing `~/.claude.json`
- Whether the config resolution order (global vs project-scope vs repo `.mcp.json`) is documented and whether repo-level should always win
- Whether a stale empty `accessToken` in `~/.claude/.credentials.json` can be cleared via a CLI command rather than manual JSON editing
- Whether the MCP gateway should return a more informative 401 body (e.g. hint that the token is not a JWT) to help clients recover faster
