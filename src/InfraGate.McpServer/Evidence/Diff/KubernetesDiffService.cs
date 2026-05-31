using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.McpServer.Models;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer.Diff;

internal static class KubernetesDiffService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] DiffHeaderLines = ["--- live", "+++ proposed"];

    public static async Task<KubernetesPlanDiff[]> BuildDiffsAsync(
        IKubernetes client,
        string operation,
        IReadOnlyList<KubernetesObjectRef> objects,
        IReadOnlyList<KubernetesPlanDryRunObject> dryRunObjects,
        CancellationToken cancellationToken)
    {
        var diffs = new List<KubernetesPlanDiff>();
        var dryRunByObject = dryRunObjects.ToDictionary(obj => obj.Object, StringComparer.Ordinal);

        foreach (var obj in objects)
        {
            var liveJson = await ReadComparableLiveJsonAsync(client, operation, obj, cancellationToken).ConfigureAwait(false);
            var proposedJson = ProposedJson(operation, obj, dryRunByObject);
            diffs.Add(BuildDiff(obj, liveJson, proposedJson));
        }

        return diffs.ToArray();
    }

    public static async Task<string?> FindDriftAsync(
        IKubernetes client,
        string operation,
        IReadOnlyList<KubernetesPlanDiff> diffs,
        CancellationToken cancellationToken)
    {
        if (diffs.Count == 0)
        {
            return "Recorded diff data is empty.";
        }

        foreach (var diff in diffs)
        {
            var liveJson = await ReadComparableLiveJsonAsync(client, operation, diff.Object, cancellationToken).ConfigureAwait(false);
            var normalizedLiveJson = liveJson is null ? null : KubernetesObjectNormalizer.NormalizeJson(liveJson);

            if (SameJson(diff.LiveObjectJson, normalizedLiveJson))
            {
                continue;
            }

            return $"Live Kubernetes state no longer matches recorded diff for {FormatObjectRef(diff.Object)}. Re-request the plan before applying.";
        }

        return null;
    }

    public static KubernetesPlanDiff BuildDiff(KubernetesObjectRef obj, string? liveJson, string? proposedJson)
    {
        var normalizedLiveJson = liveJson is null ? null : KubernetesObjectNormalizer.NormalizeJson(liveJson);
        var normalizedProposedJson = proposedJson is null ? null : KubernetesObjectNormalizer.NormalizeJson(proposedJson);
        var changes = ComparePaths(normalizedLiveJson, normalizedProposedJson);
        var changeType = ChangeType(normalizedLiveJson, normalizedProposedJson);

        return new KubernetesPlanDiff(
            obj,
            changeType,
            Summary(obj, changeType),
            BuildUnifiedDiff(normalizedLiveJson, normalizedProposedJson),
            normalizedLiveJson,
            normalizedProposedJson,
            changes.AddedPaths,
            changes.RemovedPaths,
            changes.ChangedPaths);
    }

    private static string? ProposedJson(
        string operation,
        KubernetesObjectRef obj,
        Dictionary<string, KubernetesPlanDryRunObject> dryRunByObject)
    {
        if (string.Equals(operation, KubernetesConventions.MutationOperations.Delete, StringComparison.Ordinal))
        {
            return null;
        }

        var key = FormatObjectRef(obj);
        if (!dryRunByObject.TryGetValue(key, out var dryRunObject))
        {
            throw new InvalidOperationException($"Dry-run output is missing object '{key}'.");
        }

        return dryRunObject.ResponseJson;
    }

    private static async Task<string?> ReadComparableLiveJsonAsync(
        IKubernetes client,
        string operation,
        KubernetesObjectRef obj,
        CancellationToken cancellationToken)
    {
        try
        {
            object liveObject = operation switch
            {
                KubernetesConventions.MutationOperations.Scale => await client.AppsV1.ReadNamespacedDeploymentScaleAsync(
                    obj.Name,
                    obj.Namespace,
                    cancellationToken: cancellationToken).ConfigureAwait(false),
                _ => await ReadLiveObjectAsync(client, obj, cancellationToken).ConfigureAwait(false)
            };

            return JsonSerializer.Serialize(liveObject, JsonOptions);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    private static Task<object> ReadLiveObjectAsync(
        IKubernetes client,
        KubernetesObjectRef obj,
        CancellationToken cancellationToken)
    {
        return (obj.ApiVersion, obj.Kind) switch
        {
            (KubernetesConventions.KubernetesResources.AppsV1, KubernetesConventions.KubernetesResources.Deployment) =>
                ReadDeploymentAsync(client, obj, cancellationToken),
            (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.Service) =>
                ReadServiceAsync(client, obj, cancellationToken),
            (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.ConfigMap) =>
                ReadConfigMapAsync(client, obj, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported object for diff: {FormatObjectRef(obj)}.")
        };
    }

    private static async Task<object> ReadDeploymentAsync(
        IKubernetes client,
        KubernetesObjectRef obj,
        CancellationToken cancellationToken) =>
        await client.AppsV1.ReadNamespacedDeploymentAsync(
            obj.Name,
            obj.Namespace,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    private static async Task<object> ReadServiceAsync(
        IKubernetes client,
        KubernetesObjectRef obj,
        CancellationToken cancellationToken) =>
        await client.CoreV1.ReadNamespacedServiceAsync(
            obj.Name,
            obj.Namespace,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    private static async Task<object> ReadConfigMapAsync(
        IKubernetes client,
        KubernetesObjectRef obj,
        CancellationToken cancellationToken) =>
        await client.CoreV1.ReadNamespacedConfigMapAsync(
            obj.Name,
            obj.Namespace,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    private static bool IsNotFound(Exception ex)
    {
        return ex is KubernetesException { Status.Code: 404 } ||
               ex is HttpOperationException { Response.StatusCode: HttpStatusCode.NotFound };
    }

    private static bool SameJson(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static string ChangeType(string? liveJson, string? proposedJson)
    {
        if (liveJson is null && proposedJson is not null)
        {
            return KubernetesConventions.DiffChangeTypes.Create;
        }

        if (liveJson is not null && proposedJson is null)
        {
            return KubernetesConventions.DiffChangeTypes.Delete;
        }

        if (SameJson(liveJson, proposedJson))
        {
            return KubernetesConventions.DiffChangeTypes.NoOp;
        }

        return KubernetesConventions.DiffChangeTypes.Update;
    }

    private static string Summary(KubernetesObjectRef obj, string changeType)
    {
        return changeType switch
        {
            KubernetesConventions.DiffChangeTypes.Create => $"{FormatObjectRef(obj)} will be created.",
            KubernetesConventions.DiffChangeTypes.Update => $"{FormatObjectRef(obj)} will be updated.",
            KubernetesConventions.DiffChangeTypes.Delete => $"{FormatObjectRef(obj)} will be deleted.",
            _ => $"{FormatObjectRef(obj)} has no normalized changes."
        };
    }

    private static PathChanges ComparePaths(string? liveJson, string? proposedJson)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        var live = liveJson is null ? null : JsonNode.Parse(liveJson);
        var proposed = proposedJson is null ? null : JsonNode.Parse(proposedJson);

        CompareNodes(live, proposed, string.Empty, added, removed, changed);

        return new PathChanges(
            added.Order(StringComparer.Ordinal).ToArray(),
            removed.Order(StringComparer.Ordinal).ToArray(),
            changed.Order(StringComparer.Ordinal).ToArray());
    }

    private static void CompareNodes(
        JsonNode? live,
        JsonNode? proposed,
        string path,
        List<string> added,
        List<string> removed,
        List<string> changed)
    {
        if (live is null && proposed is null)
        {
            return;
        }

        if (live is null)
        {
            CollectLeafPaths(proposed, path, added);
            return;
        }

        if (proposed is null)
        {
            CollectLeafPaths(live, path, removed);
            return;
        }

        if (live is JsonObject liveObject && proposed is JsonObject proposedObject)
        {
            CompareObjects(liveObject, proposedObject, path, added, removed, changed);
            return;
        }

        if (live is JsonArray liveArray && proposed is JsonArray proposedArray)
        {
            CompareArrays(liveArray, proposedArray, path, added, removed, changed);
            return;
        }

        if (!JsonNode.DeepEquals(live, proposed))
        {
            changed.Add(PathOrRoot(path));
        }
    }

    private static void CompareObjects(
        JsonObject live,
        JsonObject proposed,
        string path,
        List<string> added,
        List<string> removed,
        List<string> changed)
    {
        var keys = live.Select(property => property.Key)
            .Union(proposed.Select(property => property.Key), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var nextPath = AppendPath(path, key);
            var liveHasKey = live.ContainsKey(key);
            var proposedHasKey = proposed.ContainsKey(key);

            if (!liveHasKey)
            {
                CollectLeafPaths(proposed[key], nextPath, added);
                continue;
            }

            if (!proposedHasKey)
            {
                CollectLeafPaths(live[key], nextPath, removed);
                continue;
            }

            CompareNodes(live[key], proposed[key], nextPath, added, removed, changed);
        }
    }

    private static void CompareArrays(
        JsonArray live,
        JsonArray proposed,
        string path,
        List<string> added,
        List<string> removed,
        List<string> changed)
    {
        var sharedCount = Math.Min(live.Count, proposed.Count);
        for (var i = 0; i < sharedCount; i++)
        {
            CompareNodes(live[i], proposed[i], AppendPath(path, i.ToString()), added, removed, changed);
        }

        for (var i = sharedCount; i < proposed.Count; i++)
        {
            CollectLeafPaths(proposed[i], AppendPath(path, i.ToString()), added);
        }

        for (var i = sharedCount; i < live.Count; i++)
        {
            CollectLeafPaths(live[i], AppendPath(path, i.ToString()), removed);
        }
    }

    private static void CollectLeafPaths(JsonNode? node, string path, List<string> paths)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    CollectLeafPaths(property.Value, AppendPath(path, property.Key), paths);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    CollectLeafPaths(array[i], AppendPath(path, i.ToString()), paths);
                }
                break;
            default:
                paths.Add(PathOrRoot(path));
                break;
        }
    }

    private static string BuildUnifiedDiff(string? liveJson, string? proposedJson)
    {
        if (SameJson(liveJson, proposedJson))
        {
            return "No diff.";
        }

        var liveLines = YamlLines(liveJson);
        var proposedLines = YamlLines(proposedJson);
        var diffLines = BuildLineDiff(liveLines, proposedLines);

        return string.Join(Environment.NewLine, DiffHeaderLines.Concat(diffLines));
    }

    private static string[] YamlLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var yaml = KubernetesObjectNormalizer.ToYaml(json);

        return string.IsNullOrWhiteSpace(yaml)
            ? []
            : yaml.Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    private static IEnumerable<string> BuildLineDiff(string[] liveLines, string[] proposedLines)
    {
        var lcs = LongestCommonSubsequence(liveLines, proposedLines);
        var i = 0;
        var j = 0;

        foreach (var common in lcs)
        {
            while (i < liveLines.Length && !string.Equals(liveLines[i], common, StringComparison.Ordinal))
            {
                yield return "-" + liveLines[i++];
            }

            while (j < proposedLines.Length && !string.Equals(proposedLines[j], common, StringComparison.Ordinal))
            {
                yield return "+" + proposedLines[j++];
            }

            yield return " " + common;
            i++;
            j++;
        }

        while (i < liveLines.Length)
        {
            yield return "-" + liveLines[i++];
        }

        while (j < proposedLines.Length)
        {
            yield return "+" + proposedLines[j++];
        }
    }

    private static string[] LongestCommonSubsequence(string[] left, string[] right)
    {
        var lengths = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var result = new List<string>();
        var x = 0;
        var y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                result.Add(left[x]);
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return result.ToArray();
    }

    private static string AppendPath(string path, string segment) =>
        path + "/" + EscapePathSegment(segment);

    private static string EscapePathSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string PathOrRoot(string path) =>
        string.IsNullOrEmpty(path) ? "/" : path;

    private static string FormatObjectRef(KubernetesObjectRef obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";

    private sealed record class PathChanges(
        string[] AddedPaths,
        string[] RemovedPaths,
        string[] ChangedPaths);
}
