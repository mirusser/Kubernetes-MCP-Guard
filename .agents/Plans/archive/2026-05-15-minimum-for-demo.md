Minimum strong demo:

1. Policy validator
- Reject privileged containers.
- Reject hostPath.
- Reject hostNetwork, hostPID, hostIPC.
- Reject unsafe capabilities.
- Reject latest images, or at least warn/block by profile.
- Reject dangerous Service types unless policy allows them.
2. Server-side dry-run
- Before creating an approval challenge.
- Again before apply.
3. Diff in browser approval
- Human sees exactly what will change.
- Not model-written summary.
- Not just raw YAML.
4. No default force: true
- Normal apply should not forcibly take ownership of fields.
- Force apply should be an explicitly dangerous mode.
5. Redacted model-visible responses
- Do not return local pending file paths.
- Do not blindly echo full manifests with sensitive ConfigMap data.
- Keep full details in browser/audit, not necessarily MCP response.
6. Tests proving the safety model
- Plan hash mismatch fails.
- Expired approval fails.
- Already-applied plan fails.
- Dangerous manifest fails.
- Modified pending plan fails.
- Wrong user approval fails.
- Dry-run failure blocks approval/apply.

That is enough to say: “This is not just an idea; here is a working prototype.”

What I would call the proposal

Something like:

```text
Approval-Gated Destructive MCP Actions
```

or more specific:

```text
Human-Verified Mutation Plans for Kubernetes MCP Servers
```

or if you want it to sound more architectural:

```text
A Plan–Policy–Approval Architecture for Safe MCP Mutations
```

Avoid branding it too much around your repo name at first. The architecture is the thing. The repo is the reference implementation.