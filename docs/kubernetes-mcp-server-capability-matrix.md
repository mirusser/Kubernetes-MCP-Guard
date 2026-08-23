# kubernetes-mcp-server v0.0.66 Capability Contract

This document records InfraGate's classification of the 52 tool names that v0.0.66 can expose
across its eight toolsets. It is an admission contract, not a restatement of upstream MCP
annotations. The executable contract, including the exact input-schema SHA-256 and complete
annotation SHA-256 for every row, is
`KubernetesMcpServerCapabilityManifest.V0066` in
`src/InfraGate.McpGateway/McpTransport/Dispatch/KubernetesMcpServerCapabilityManifest.cs`.

Artifact identity:

- Version: `v0.0.66`
- Linux amd64 SHA-256: `692a7b283a96140311fd46f13b8373657b2e9bfe660a36bb6434e8c42d899dbc`
- Classified names: 52
- Reproducible single-context `tools/list` snapshot: 50 tools
- Conditional names outside that snapshot: `configuration_contexts_list` and `targets_list`

`readOnlyHint` and `destructiveHint` are captured drift inputs only. In particular,
`--disable-destructive` is not a write barrier: `helm_install`, `pods_run`,
`tekton_pipeline_start`, `tekton_task_start`, and `tekton_taskrun_restart` are writes with
`destructiveHint=false`.

## Role treatment

| Role | Intent/evidence treatment | Authorization and output | v0.0.66 runtime status |
| --- | --- | --- | --- |
| Public viewer | No Mutation Intent; bounded sanitized proxy response | `mcp:tools.readonly` or `mcp:tools.read`; 256 KiB | Admitted only for the three existing exact-name tools |
| Hidden executor | Tool-specific intent codec; genuine plan-only evidence required | `mcp:tools.write`; 256 KiB | Classified only; no executor is configured because v0.0.66 lacks the required evidence |
| Elevated exec | Exact pod/argv intent; separately approved non-reversible evidence model required | Proposed `mcp:tools.exec`; 256 KiB | Not configured and not authorized |
| Disabled | No intent codec, evidence strategy, scope, or output allowance | No scopes; 0 bytes | Rejected |

The hidden-executor and elevated-exec labels describe the only process role in which a future,
evidence-compatible release could be considered. They do not admit the v0.0.66 tools for
execution.

## Complete classification

`R` and `D` are the captured upstream `readOnlyHint` and `destructiveHint` values.

| Toolset | Tool | R | D | Risk class | Process role |
| --- | --- | ---: | ---: | --- | --- |
| config | `configuration_contexts_list` | true | false | Sensitive read | Disabled |
| config | `configuration_view` | true | false | Sensitive read | Disabled |
| config | `targets_list` | true | false | Sensitive read | Disabled |
| core | `events_list` | true | false | Sensitive read | Disabled |
| core | `namespaces_list` | true | false | Sensitive read | Disabled |
| core | `nodes_log` | true | false | Sensitive read | Disabled |
| core | `nodes_stats_summary` | true | false | Sensitive read | Disabled |
| core | `nodes_top` | true | false | Sensitive read | Disabled |
| core | `pods_delete` | false | true | Destructive mutation | Hidden executor |
| core | `pods_exec` | false | true | Command execution | Elevated exec |
| core | `pods_get` | true | false | Bounded read | Public viewer |
| core | `pods_list` | true | false | Sensitive read | Disabled |
| core | `pods_list_in_namespace` | true | false | Bounded read | Public viewer |
| core | `pods_log` | true | false | Bounded read | Public viewer |
| core | `pods_run` | false | false | Finite mutation | Hidden executor |
| core | `pods_top` | true | false | Sensitive read | Disabled |
| core | `projects_list` | true | false | Sensitive read | Disabled |
| core | `resources_create_or_update` | false | true | Destructive mutation | Hidden executor |
| core | `resources_delete` | false | true | Destructive mutation | Hidden executor |
| core | `resources_get` | true | false | Sensitive read | Disabled |
| core | `resources_list` | true | false | Sensitive read | Disabled |
| core | `resources_scale` | false | true | Finite mutation | Hidden executor |
| helm | `helm_install` | false | false | External-system write | Hidden executor |
| helm | `helm_list` | true | false | Sensitive read | Disabled |
| helm | `helm_uninstall` | false | true | External-system write | Hidden executor |
| kcp | `kcp_workspace_describe` | true | false | Sensitive read | Disabled |
| kcp | `kcp_workspaces_list` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_logs` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_mesh_status` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_mesh_traffic_graph` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_metrics` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_pod_performance` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_resource_details` | true | false | Sensitive read | Disabled |
| kiali | `kiali_get_trace_details` | true | false | Sensitive read | Disabled |
| kiali | `kiali_list_mesh_clusters` | true | false | Sensitive read | Disabled |
| kiali | `kiali_list_traces` | true | false | Sensitive read | Disabled |
| kiali | `kiali_manage_istio_config` | false | true | External-system write | Disabled |
| kiali | `kiali_manage_istio_config_read` | true | false | Sensitive read | Disabled |
| kubevirt | `vm_clone` | false | true | External-system write | Disabled |
| kubevirt | `vm_create` | false | true | External-system write | Disabled |
| kubevirt | `vm_guest_info` | true | false | Sensitive read | Disabled |
| kubevirt | `vm_lifecycle` | false | true | External-system write | Disabled |
| kubevirt | `vm_troubleshoot` | true | false | Sensitive read | Disabled |
| netobserv | `netobserv_export_flows` | true | false | Sensitive read | Disabled |
| netobserv | `netobserv_get_flow_metrics` | true | false | Sensitive read | Disabled |
| netobserv | `netobserv_list_flows` | true | false | Sensitive read | Disabled |
| tekton | `tekton_pipeline_start` | false | false | External-system write | Disabled |
| tekton | `tekton_pipelinerun_lifecycle` | false | true | External-system write | Disabled |
| tekton | `tekton_pipelinerun_logs` | true | false | Sensitive read | Disabled |
| tekton | `tekton_task_start` | false | false | External-system write | Disabled |
| tekton | `tekton_taskrun_logs` | true | false | Sensitive read | Disabled |
| tekton | `tekton_taskrun_restart` | false | false | External-system write | Disabled |

## Admission behavior

The optional viewer process is admitted only when every advertised tool:

1. has a classified exact name;
2. matches the pinned canonical input-schema hash;
3. matches the pinned canonical full-annotation hash; and
4. is assigned to the public-viewer role.

A failed initial publication exposes none of that source's tools. A failed regeneration keeps the
last admitted generation and marks the source degraded. The existing TOML validation still
requires exactly `pods_get`, `pods_list_in_namespace`, and `pods_log`, so a config change cannot
expand the viewer surface before catalog admission runs.

The required live contract check is:

```bash
rtk test env INFRA_GATE_REQUIRE_KUBERNETES_MCP_CAPABILITY_CONTRACT=1 \
  dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj \
  --filter FullyQualifiedName~InstalledV0066_ToolsListMatchesPinnedSingleClusterSnapshot_WhenRequired
```

It launches the checksum-pinned installed binary, enables all eight toolsets in single-context
mode, captures the real 50-tool `tools/list` response, and validates every schema and annotation
against the executable contract. The two target-provider-dependent names are classified from the
same tagged source contract but are intentionally unavailable in this single-context runtime
profile.
