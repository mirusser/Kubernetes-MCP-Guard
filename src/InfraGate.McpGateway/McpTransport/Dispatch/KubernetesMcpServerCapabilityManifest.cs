using System.Buffers;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InfraGate.McpGateway;

internal enum KubernetesMcpServerProcessRole
{
    PublicViewer,
    HiddenExecutor,
    ElevatedExec,
    Disabled
}

internal enum KubernetesMcpServerRiskClass
{
    BoundedRead,
    SensitiveRead,
    FiniteMutation,
    DestructiveMutation,
    CommandExecution,
    ExternalSystemWrite
}

internal sealed record class KubernetesMcpServerToolCapability(
    string Name,
    string Toolset,
    KubernetesMcpServerRiskClass RiskClass,
    KubernetesMcpServerProcessRole AllowedProcessRole,
    bool IsReadOnly,
    bool IsDestructive,
    string InputSchemaSha256,
    string AnnotationsSha256,
    string IntentCodec,
    string EvidenceStrategy,
    IReadOnlyList<string> AuthorizationScopes,
    int MaximumOutputBytes,
    bool InPinnedSingleClusterSnapshot);

/// <summary>
/// Repository-owned admission contract for the pinned kubernetes-mcp-server release. Upstream
/// annotations are captured inputs, never authorization: each tool also has an InfraGate-owned
/// risk class, process role, intent/evidence treatment, scopes, and output bound.
/// </summary>
internal sealed class KubernetesMcpServerCapabilityManifest
{
    private const int BoundedOutputBytes = 256 * 1024;
    private const string UnavailableEvidence = "plan-only-evidence-unavailable-v0.0.66";
    private const string DisabledCodec = "none-disabled";
    private const string DisabledEvidence = "unsupported-disabled";

    private static readonly IReadOnlyList<string> ReadScopes =
        new[] { "mcp:tools.readonly", "mcp:tools.read" };

    private static readonly IReadOnlyList<string> WriteScopes = new[] { "mcp:tools.write" };
    private static readonly IReadOnlyList<string> ExecScopes = new[] { "mcp:tools.exec" };
    private static readonly IReadOnlyList<string> NoScopes = Array.Empty<string>();

    public static KubernetesMcpServerCapabilityManifest V0066 { get; } = new(
        "v0.0.66",
        "692a7b283a96140311fd46f13b8373657b2e9bfe660a36bb6434e8c42d899dbc",
        [
            Disabled("configuration_contexts_list", "config", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "0b70bdb2238c3a5875752ac4b81514a555e8383508a534f7f2029a15063ff091",
                inPinnedSingleClusterSnapshot: false),
            Disabled("configuration_view", "config", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "3679d0c6ab33c6b6dda2fc515d839923afd198ee52fec7245bffd341372e7151",
                "9c6cf7921388010ea6430d2e49c6b3e0f60a4240dc3004897ef48d1ef459d083"),
            Disabled("targets_list", "config", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "38fc31e519535b0d9331a1f086750ef42667f493d8ad7355c0153cada97fbdbd",
                inPinnedSingleClusterSnapshot: false),

            Disabled("events_list", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "1a8173b7e6fb1bbb7f1f013518beb1f5f6d19c5b11d3e587a1f58bebeedc2681",
                "437d08d314e88dfb331907dda85345d68f6c0efb54008182a8eed9adbf7d66e9"),
            Disabled("namespaces_list", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "90ce3153949e6207da2d6464342e2118dedd49b4b1fbe5b6a9800c285a6f3d3c",
                "159bd1643d0d4463412543d55378cd418d9c957f284b89bdc3eeb488ca4cb712"),
            Disabled("nodes_log", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "00878387bdf1465d2bd170b3a214d38b352dfc2e06248884dd4aaad0a34e3df5",
                "1c42ec52ba7afd7167d365aee50be44f674a5e535a4dc8b4578e3c7e3e74f87e"),
            Disabled("nodes_stats_summary", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "c1431487df01cafddca017f256fc85325124e6e637586067228977a82e1aeaad",
                "0edac992ad624a89fad469d0048e4e6e6070653bd551b82c4cc077dac584ebe7"),
            Disabled("nodes_top", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "2c53d4da1b1eee875988508f25a729b9291516d67326cbaac51ed87aba619eca",
                "5f33a2a8f69d44eac272d8526e62fd58428f862476b2ca25b6a5f66af691b43b"),
            Executor("pods_delete", "core", KubernetesMcpServerRiskClass.DestructiveMutation,
                false, true,
                "d1a2c859ec1f23526d3f97d7924c46d604735aebc74785791e3d5d5ac8756fb3",
                "660c44a31fd7a9be048dfe52eb5545376efec8c3ccd16863e742b353babaff83",
                "pod-delete-intent"),
            ElevatedExec("pods_exec", "core",
                "0d5cd89b10846c0287848da90ddf48e72f50f9135f26aa21a8fd7420219c0d59",
                "9f7f42e5618f82d05132e29ac3b245406daa4fc08f18e9fe38559cea20b3b8c4"),
            Viewer("pods_get", "core",
                "fdc6370534b573d56f0800001bb963578c01fbe435e66ea4313212f05d1f5c49",
                "eb36eab842a9e35c19d490db8295ea77a539af9746fa26ae0ec38a978ca07305"),
            Disabled("pods_list", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "17290c1dc129aa1ca6427e5819d22d6f7c8334e3313f84ea54f4696e1af559cd",
                "66ca6ab4dd0843c227fc280fec739b1d54e5a7dab69a8a1485bc2ec922f16b8d"),
            Viewer("pods_list_in_namespace", "core",
                "1c2a5fedc083688088f7a54fd950d43513bd83c07f738d0526984ffafbbe394d",
                "b88a296b18eefd7b00eb1ec01297e0080ac0fa160874e52a384c9aa6a4c928d6"),
            Viewer("pods_log", "core",
                "38f7eac29287db17298e8b7b47f99b989231fc586e9e9f4b7ec07b8f64d8cf80",
                "09c672fbf5d509cd384ba5b1cf786a2dc6b999fd7d6470c2faf846b9cdc83eb5"),
            Executor("pods_run", "core", KubernetesMcpServerRiskClass.FiniteMutation,
                false, false,
                "e7df42af4c31b97a4f598d75630eaf884cfa254c1528bd201b8083955ffac670",
                "08304ab79584efb1362d2c33644687baddd331f7cb84d6672b2cb0dcfa53fa9c",
                "pod-run-intent"),
            Disabled("pods_top", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "29b6573c2bd834ea960e18a557ecd5d43430df10531ce4c7277d5ed28855b563",
                "efc4e74b0fc7dfd42cb05411441cc0445773d8a0e2ae064b444a57e25a19ad31"),
            Disabled("projects_list", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "a4aa7e7b4bac0103f873b53fab381d0806418509ccbaf9d2797241aa8ead6506"),
            Executor("resources_create_or_update", "core", KubernetesMcpServerRiskClass.DestructiveMutation,
                false, true,
                "5b9939d72df67639b7d10fbdaeeb2d32eb0635d2b058e0c67e851874818ac86a",
                "23c729e5c018aefe5dde05e60a5faa2076f36675758d2034ed78ff49b1d7c84f",
                "kubernetes-resource-intent"),
            Executor("resources_delete", "core", KubernetesMcpServerRiskClass.DestructiveMutation,
                false, true,
                "0b7b8de023fbf7f577ccf384fe2de172016dde1bd79d05599f395503c0a52084",
                "a29032599df18b7bb45c32784efd2ca929a02ec0012254cfbd23d5775c8a141a",
                "kubernetes-resource-delete-intent"),
            Disabled("resources_get", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "c7183e28a88b01bcd4d3ca1379555983b45ccf8ce5b742f415e313434a7f20ec",
                "e293bf9df745efb770e5932e1401e1e1625d4f6dbe60ed431f10e280d89d5148"),
            Disabled("resources_list", "core", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "b9667dbc1d7510df640938bf330d6f9e19ba59905abc7b01f5be8d2d6ba13056",
                "ed4cd05bdbdc80ed59ebe392a1b9fa2f1a295d91f0ef4d0b094a588023af398a"),
            Executor("resources_scale", "core", KubernetesMcpServerRiskClass.FiniteMutation,
                false, true,
                "af9c5d832fa758a02ee98f7237870fdf0a8198ad886ebe6998f791697aa057e3",
                "58dbacf533f02f5826ab8db0e57e2fcaa08b0efca76b6679d5ef379705905a26",
                "kubernetes-resource-scale-intent"),

            Executor("helm_install", "helm", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, false,
                "374f18b8008a857bbb736342d40c6532fb4b38c6f5718261112c864c5bbaa1c1",
                "b8765fc1a002f8fc1346bf270ab4c1f97f687fc028861bbb555cbf0c689615b6",
                "helm-install-intent"),
            Disabled("helm_list", "helm", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "dfff9d9f30fb244a8ca2b2e778a0e886976bed5670ab61a125009f8a5e19700e",
                "30f431c6ef47be33d0baff24bb11b222eaf31c3d37e446ed9bdba4b4726910ca"),
            Executor("helm_uninstall", "helm", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "9b62ee0c9cdcd915deeb6488ef7d6e92fd7ba4bee5d4b86b758285dbc370706e",
                "63824cecf0d68d531194e875b468a0a27690f81f0e74fc85067ce3d8df1028f3",
                "helm-uninstall-intent"),

            Disabled("kcp_workspace_describe", "kcp", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "9234bb27d206123d037506ef0b7aca4bfb0913139cdda9c9e81b2937e2d10b5a",
                "c9664b883755dfbe07f9b57a0664fcbf017afbcd6fe24f8f4c303633da288f6a"),
            Disabled("kcp_workspaces_list", "kcp", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "74055d305a2593499dd163d8afdbf5f05c81c11247060f011f4c081b14638d1a"),

            Disabled("kiali_get_logs", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "8e556d2328f382607750cbadc31abf7fbaf2513c7eb7c4bb4ceae0c4dd28362b",
                "2b03ecc4dfd80dcc30edb70d7cfadae57b5594927946548f484c4fddba37f8a5"),
            Disabled("kiali_get_mesh_status", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "ef4980af55a72810e5ba315d1e43e3d400bdb7895b76ee0ab35eeebb618d2a0c"),
            Disabled("kiali_get_mesh_traffic_graph", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "eddf92b1aef9807fd03cbc122627d4abeabfa05d9013af85370f793689e702ce",
                "05e3a5a502e13e251db40bb36ea9cd8d5909c8c6a25492e1f618356d69d762c5"),
            Disabled("kiali_get_metrics", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "9918f6bcb112a5d611abfd5d403508e93a03f2c917d724c6a1615c7555ed81f8",
                "74ae72d39795e11ca048432b0dc5793fc87066b93f2bd905baeee35b7658faa2"),
            Disabled("kiali_get_pod_performance", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "5159726545f1ad73ec5f7c01554c1ceec9aead7f7da36440a9ba231fbff87930",
                "1bc47f617e931a3dc52b57aa7bb04672736cce7497dad2a4a9a6bde39c03522b"),
            Disabled("kiali_get_resource_details", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "f570b49760f30f27398405d07c54f5a8ed9484f616e50f0f067f30cd846765c7",
                "74f3a0fe8476ebeb582ee122d4dcd22ca0e023e19d15f6f8ba700572a5026b74"),
            Disabled("kiali_get_trace_details", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "7b13549cddb24cdb51fa464841dbd759b18144a431a828463c6eda8150ddce69",
                "62eadd64a15a36cecabadd418d91d9839b7682b95d48cf3dbc12cf5cc3b6b7fa"),
            Disabled("kiali_list_mesh_clusters", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "efddc7bd8bbcef73a14eb1ace1ffdaec81e518ef1e13c1e9271d0b8acb694a49",
                "33e55c70f281c19265264f84731e9ca19574277019ca533ce92c8dcba4ef7389"),
            Disabled("kiali_list_traces", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "ad66782f108680359023fe0781d23d2f4a323fc85964ce6ed773513ad9c88321",
                "2f72705e3cc9cd234be79b8738a73bf8dd2757152c5e80591f70ddc3332eceb3"),
            Disabled("kiali_manage_istio_config", "kiali", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "9a7c9daca3791c155232d17f0cd7930f582d22e29c0edbe6ddad46cbbf1893ee",
                "43db492fe92b376a102ba7d214b71c80f20dbeb29ab49824d1345365f69830c3"),
            Disabled("kiali_manage_istio_config_read", "kiali", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "714bfcabc9eb2580b4cf04499f41f10ad73f8dd23d3249d7dbb366b106dbffb3",
                "59f36cca2cc746b0a50bd378d854ae17dfbc029c1cd299892800ec73c492f081"),

            Disabled("vm_clone", "kubevirt", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "5a05552e726c60e6566412abe8d76035b0756186b9c2a02c63850d6eb5c39595",
                "a97cc8c67e2f5153f7ba5a2c48f763b45d39bb16c5b103c7f5136ca3aa81a23e"),
            Disabled("vm_create", "kubevirt", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "48a419a4c3b3e93be364cc56d3984629819d46bf5c0863cc7dcfa097ce09f4c2",
                "623d022796d367f310e2fab3a112300b452fa3831f9cbd082adbc86c32e48b93"),
            Disabled("vm_guest_info", "kubevirt", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "20e17e166028776115ba4e1f6e4894af53d0400342e3bbbe91797c4822d41613",
                "203453fc26626aa2402a7638faf257551b54a48742fdc064f64dcc96f4f62d95"),
            Disabled("vm_lifecycle", "kubevirt", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "8dfc29dff5f80af454913ba533cd88acb67fa350435e860a483bc9f97664c111",
                "9bcc32c9954e96fe3e601c67fddb7533f44f674e19783eb1e035f9762a6086a6"),
            Disabled("vm_troubleshoot", "kubevirt", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "4c9f11a63e870ce70e0a20473e15514456cb6a53fbe6c0e210d55df74f858be5",
                "9b7eb07a3c3a1dc45b1d529f2e20d38a81e15a57ad54d4c91c2c863377e86039"),

            Disabled("netobserv_export_flows", "netobserv", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "5addf7e90bea247869cfd52a929241f67f64ebe2bac6f6d95318adb083fa1fa1",
                "f0188c60c197c9f9537426307066ed6358c0119ef37d7dd45462f4698fb449fa"),
            Disabled("netobserv_get_flow_metrics", "netobserv", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "94921ca2ce420cbeb5cd57ae60cbac9f4c448647414a3ca1df237df0404bea22",
                "a19af1d466b635237b57d7b587a5dbb2678e8a7f1312c886ecc07c69fe27ebbe"),
            Disabled("netobserv_list_flows", "netobserv", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "499f4cda243ce5437ca56ec45725094bf0a82b7aad644a96e9d85ddb601e86c6",
                "f210360f2f5f153b1744bdddea0065818fcfb7be58f0ee9977564ec07ad854c9"),

            Disabled("tekton_pipeline_start", "tekton", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, false,
                "133cd8e486a93b83ce94ceff00af84072106d8ad201832a0cdc6c64f43d76533",
                "f355c7a611b525041886579b822a0558ce7d69cf3d2206dcf8ced1a417835773"),
            Disabled("tekton_pipelinerun_lifecycle", "tekton", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, true,
                "b253c4432bb7b3316ee6441d54227d908e42cc3e9a4e344b052efa419c317390",
                "86e4ac6da4fbc3abc8d55f98035ef82bfa7e4f21c4c68531b627cb18447e0c2a"),
            Disabled("tekton_pipelinerun_logs", "tekton", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "55cdd2b6748eb981d593bfdaef4b3d9862001f1bb072ad310b6b671766b1e370",
                "846cc074955684fdaf6d0ab6ac65feb5d7ed5a6f5d246873a43beb59eee060fb"),
            Disabled("tekton_task_start", "tekton", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, false,
                "29a705d87d3404f8d4de96f1f474902d9483067491f52e240cb059e5e3a36905",
                "52266c1ddb34a79b07a2f05602d2c86df0c15b4e1c772f33af598c958a1cc3b9"),
            Disabled("tekton_taskrun_logs", "tekton", KubernetesMcpServerRiskClass.SensitiveRead,
                true, false,
                "d8eeeaadffae1e0dcecb0979dc0946eaa169a78535e0cd89b636fe70f7be81ec",
                "62b972fec514005ed99099995b230eba06103b493e2b1999cd992a19cf282cca"),
            Disabled("tekton_taskrun_restart", "tekton", KubernetesMcpServerRiskClass.ExternalSystemWrite,
                false, false,
                "fc711eb4a29ddbd0644bc0b011e974c8b98f8b5b33b80f85ee094341055e2b32",
                "c018ceef42b1ef96982086565fe2882ceb8da0e99f36a77e4c1ba22ee162073e")
        ]);

    private readonly FrozenDictionary<string, KubernetesMcpServerToolCapability> toolsByName;

    private KubernetesMcpServerCapabilityManifest(
        string version,
        string linuxAmd64Sha256,
        IReadOnlyList<KubernetesMcpServerToolCapability> tools)
    {
        Version = version;
        LinuxAmd64Sha256 = linuxAmd64Sha256;
        Tools = tools;
        toolsByName = tools.ToFrozenDictionary(tool => tool.Name, StringComparer.Ordinal);
    }

    public string Version { get; }

    public string LinuxAmd64Sha256 { get; }

    public IReadOnlyList<KubernetesMcpServerToolCapability> Tools { get; }

    public bool TryValidateTool(
        DownstreamTool tool,
        KubernetesMcpServerProcessRole processRole,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (!toolsByName.TryGetValue(tool.Name, out KubernetesMcpServerToolCapability? capability))
        {
            error = $"Kubernetes MCP server tool '{tool.Name}' is not classified by the pinned capability manifest.";
            return false;
        }

        if (capability.AllowedProcessRole != processRole)
        {
            error = $"Kubernetes MCP server tool '{tool.Name}' is not admitted for process role '{processRole}'.";
            return false;
        }

        return TryValidateContract(tool, capability, out error);
    }

    public bool TryValidatePinnedSingleClusterSnapshot(
        IReadOnlyList<DownstreamTool> tools,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(tools);

        KubernetesMcpServerToolCapability[] expected = Tools
            .Where(tool => tool.InPinnedSingleClusterSnapshot)
            .ToArray();
        if (tools.Count != expected.Length)
        {
            error = $"Pinned single-cluster catalog contains {tools.Count} tools; expected {expected.Length}.";
            return false;
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (DownstreamTool tool in tools)
        {
            if (!seenNames.Add(tool.Name))
            {
                error = $"Pinned single-cluster catalog contains duplicate tool '{tool.Name}'.";
                return false;
            }

            if (!toolsByName.TryGetValue(tool.Name, out KubernetesMcpServerToolCapability? capability)
                || !capability.InPinnedSingleClusterSnapshot)
            {
                error = $"Pinned single-cluster catalog contains unexpected tool '{tool.Name}'.";
                return false;
            }

            if (!TryValidateContract(tool, capability, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateContract(
        DownstreamTool tool,
        KubernetesMcpServerToolCapability capability,
        out string error)
    {
        if (tool.IsReadOnly != capability.IsReadOnly || tool.IsDestructive != capability.IsDestructive)
        {
            error = $"Kubernetes MCP server tool '{tool.Name}' annotation flags drifted from the pinned contract.";
            return false;
        }

        string inputSchemaSha256 = ComputeSha256(tool.InputSchema);
        if (!string.Equals(inputSchemaSha256, capability.InputSchemaSha256, StringComparison.Ordinal))
        {
            error = $"Kubernetes MCP server tool '{tool.Name}' input schema drifted from the pinned contract " +
                    $"(expected {capability.InputSchemaSha256}, got {inputSchemaSha256}).";
            return false;
        }

        string annotationsSha256 = tool.Annotations.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : ComputeSha256(tool.Annotations);
        if (tool.Annotations.ValueKind == JsonValueKind.Undefined
            || !string.Equals(
                annotationsSha256,
                capability.AnnotationsSha256,
                StringComparison.Ordinal))
        {
            error = $"Kubernetes MCP server tool '{tool.Name}' annotations drifted from the pinned contract " +
                    $"(expected {capability.AnnotationsSha256}, got {annotationsSha256}).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ComputeSha256(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            WriteCanonical(writer, element);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Cannot canonicalize an undefined JSON value.");
        }
    }

    private static KubernetesMcpServerToolCapability Viewer(
        string name,
        string toolset,
        string schemaSha256,
        string annotationsSha256) =>
        new(
            name,
            toolset,
            KubernetesMcpServerRiskClass.BoundedRead,
            KubernetesMcpServerProcessRole.PublicViewer,
            IsReadOnly: true,
            IsDestructive: false,
            schemaSha256,
            annotationsSha256,
            "none-read-only",
            "bounded-sanitized-proxy",
            ReadScopes,
            BoundedOutputBytes,
            InPinnedSingleClusterSnapshot: true);

    private static KubernetesMcpServerToolCapability Executor(
        string name,
        string toolset,
        KubernetesMcpServerRiskClass riskClass,
        bool isReadOnly,
        bool isDestructive,
        string schemaSha256,
        string annotationsSha256,
        string intentCodec) =>
        new(
            name,
            toolset,
            riskClass,
            KubernetesMcpServerProcessRole.HiddenExecutor,
            isReadOnly,
            isDestructive,
            schemaSha256,
            annotationsSha256,
            intentCodec,
            UnavailableEvidence,
            WriteScopes,
            BoundedOutputBytes,
            InPinnedSingleClusterSnapshot: true);

    private static KubernetesMcpServerToolCapability ElevatedExec(
        string name,
        string toolset,
        string schemaSha256,
        string annotationsSha256) =>
        new(
            name,
            toolset,
            KubernetesMcpServerRiskClass.CommandExecution,
            KubernetesMcpServerProcessRole.ElevatedExec,
            IsReadOnly: false,
            IsDestructive: true,
            schemaSha256,
            annotationsSha256,
            "pod-exec-intent",
            "non-reversible-evidence-model-unapproved",
            ExecScopes,
            BoundedOutputBytes,
            InPinnedSingleClusterSnapshot: true);

    private static KubernetesMcpServerToolCapability Disabled(
        string name,
        string toolset,
        KubernetesMcpServerRiskClass riskClass,
        bool isReadOnly,
        bool isDestructive,
        string schemaSha256,
        string annotationsSha256,
        bool inPinnedSingleClusterSnapshot = true) =>
        new(
            name,
            toolset,
            riskClass,
            KubernetesMcpServerProcessRole.Disabled,
            isReadOnly,
            isDestructive,
            schemaSha256,
            annotationsSha256,
            DisabledCodec,
            DisabledEvidence,
            NoScopes,
            MaximumOutputBytes: 0,
            inPinnedSingleClusterSnapshot);
}
