namespace InfraGate.RunProfiles;

internal static class RunProfileConventions
{
    public const string DefaultConfigPath = "deploy/run-profiles.yaml";

    public static class Commands
    {
        public const string Generate = "generate";
        public const string List = "list";
        public const string Validate = "validate";
    }

    public static class Options
    {
        public const string Config = "--config";
        public const string Output = "--output";
    }

    public static class YamlKeys
    {
        public const string AllowedNamespaces = "allowedNamespaces";
        public const string DomainAdapters = "domainAdapters";
        public const string GenericApprovalCore = "genericApprovalCore";
        public const string Version = "version";
        public const string Profiles = "profiles";
        public const string Kind = "kind";
        public const string KubeConfig = "kubeconfig";
        public const string Kubernetes = "kubernetes";
        public const string Name = "name";
        public const string ApprovalRoot = "approvalRoot";
        public const string RuntimeMode = "runtimeMode";
        public const string Type = "type";
    }

    public static class Env
    {
        public const string InfraGateEnvironment = "INFRA_GATE_ENVIRONMENT";
        public const string ApprovalRoot = "K8S_MCP_APPROVAL_ROOT";
        public const string AllowedNamespaces = "K8S_MCP_ALLOWED_NAMESPACES";
        public const string KubeConfig = "KUBECONFIG";
    }

    public static class DomainAdapterTypes
    {
        public const string Kubernetes = "kubernetes";
    }
}
