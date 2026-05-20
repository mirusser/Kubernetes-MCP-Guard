using System.Net;
using System.Text;
using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanReviewRenderer : IPlanReviewRenderer
{
    public string RenderReviewContent(IPlanReview planReview) =>
        RenderReviewContent((KubernetesPlan)planReview);

    public string RenderApprovalRequiredMessage(IPlanReview planReview, string approvalUrl, DateTimeOffset expiresAtUtc) =>
        RenderApprovalRequiredMessage((KubernetesPlan)planReview, approvalUrl, expiresAtUtc);

    private static string RenderReviewContent(KubernetesPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append(RenderObjectsCard(plan));
        if (!string.IsNullOrEmpty(plan.Manifest))
        {
            sb.Append(RenderManifestCard(plan));
        }
        sb.Append(RenderPolicyFindingsCard(plan));
        if (plan.DryRun is not null)
        {
            sb.Append(RenderDryRunCard(plan));
        }
        sb.Append(RenderDiffs(plan));
        return sb.ToString();
    }

    private static string RenderApprovalRequiredMessage(KubernetesPlan plan, string approvalUrl, DateTimeOffset expiresAtUtc)
    {
        var objects = string.Join(
            Environment.NewLine,
            plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));

        return $"""
               Approval required.
               PlanId: {plan.Id}
               Operation: {plan.Operation}
               Namespace: {plan.Namespace}
               Objects:
               {objects}
               Intent Digest: {plan.Envelope.IntentDigest.Value}
               Review Digest: {plan.Envelope.ReviewDigest.Value}
               Approval URL: {approvalUrl}
               Expires at UTC: {expiresAtUtc:O}

               Open the approval URL in a browser, sign in with the same identity, review the Gateway-rendered plan, then call execute_approved_plan again.
               """;
    }

    private static string RenderObjectsCard(KubernetesPlan plan)
    {
        var objects = string.Join(
            string.Empty,
            plan.Objects.Select(obj =>
                $"<li>{Html(obj.ApiVersion)} {Html(obj.Kind)} {Html(obj.Namespace)}/{Html(obj.Name)}</li>"));

        return $"""
                <section class="card" data-section="objects">
                  <h2>Objects</h2>
                  <ul>{objects}</ul>
                </section>
                """;
    }

    private static string RenderManifestCard(KubernetesPlan plan)
    {
        return $"""
                <section class="card" data-section="submitted-manifest">
                  <h2>Submitted Manifest</h2>
                  <details>
                    <summary>View manifest</summary>
                    <pre><code>{Html(plan.Manifest!)}</code></pre>
                  </details>
                </section>
                """;
    }

    private static string RenderPolicyFindingsCard(KubernetesPlan plan)
    {
        var body = plan.PolicyFindings.Length == 0
            ? "<p>None</p>"
            : ("<ul>" + string.Join(string.Empty,
                plan.PolicyFindings.Select(finding =>
                    $"<li><span class=\"badge badge-{Html(finding.Severity.ToLowerInvariant())}\">{Html(finding.Severity)}</span> [{Html(finding.Code)}] {Html(finding.Message)} <span class=\"kv-label\">{Html(finding.ObjectRef)}</span></li>")) + "</ul>");

        return $"""
                <section class="card" data-section="policy-findings">
                  <h2>Policy Findings</h2>
                  {body}
                </section>
                """;
    }

    private static string RenderDryRunCard(KubernetesPlan plan)
    {
        var dr = plan.DryRun!;
        var dryRunObjects = string.Join(
            string.Empty,
            dr.Objects.Select(obj => $"<li>{Html(obj.Object)}</li>"));
        var warnings = dr.Warnings.Length == 0
            ? "<li>None</li>"
            : string.Join(string.Empty, dr.Warnings.Select(warning => $"<li>{Html(warning)}</li>"));

        var statusClass = string.Equals(dr.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
            ? "success" : "error";

        return $"""
                <section class="card" data-section="dry-run-results">
                  <h2>Dry-run Results</h2>
                  <p class="{statusClass}">Server-side dry-run: {Html(dr.Status)}</p>
                  <div class="kv-grid">
                    <span class="kv-label">Checked at UTC</span>
                    <span class="kv-value">{Html(dr.CheckedAtUtc.ToString("O"))}</span>
                    <span class="kv-label">Message</span>
                    <span class="kv-value">{Html(dr.Message)}</span>
                  </div>
                  <h3>Dry-run Objects</h3>
                  <ul>{dryRunObjects}</ul>
                  <h3>Admission Warnings</h3>
                  <ul>{warnings}</ul>
                </section>
                """;
    }

    private static string RenderDiffs(KubernetesPlan plan)
    {
        if (plan.Diffs.Length == 0)
        {
            return """
                   <section class="card" data-section="diff">
                     <h2>Diff</h2>
                     <p class="error">No diff was recorded for this plan.</p>
                   </section>
                   """;
        }

        return """
               <section class="card" data-section="diff">
                 <h2>Diff</h2>
               """ + string.Join(string.Empty, plan.Diffs.Select(RenderDiff)) + "</section>";
    }

    private static string RenderDiff(KubernetesPlanDiff diff)
    {
        var paths = RenderDiffPaths(diff);

        return $"""
                <div class="diff-block">
                  <h3>{Html(diff.Summary)}</h3>
                  <div class="kv-grid">
                    <span class="kv-label">Change</span>
                    <span class="kv-value">{Html(diff.ChangeType)}</span>
                    <span class="kv-label">Object</span>
                    <span class="kv-value">{Html(diff.Object.ApiVersion)} {Html(diff.Object.Kind)} {Html(diff.Object.Namespace)}/{Html(diff.Object.Name)}</span>
                  </div>
                  {paths}
                  <pre><code>{Html(diff.UnifiedDiff)}</code></pre>
                </div>
                """;
    }

    private static string RenderDiffPaths(KubernetesPlanDiff diff)
    {
        return $"""
                <details>
                  <summary>Changed paths</summary>
                  <div class="kv-grid">
                    <span class="kv-label">Added</span>
                    <span class="kv-value">{RenderPathList(diff.AddedPaths)}</span>
                    <span class="kv-label">Removed</span>
                    <span class="kv-value">{RenderPathList(diff.RemovedPaths)}</span>
                    <span class="kv-label">Changed</span>
                    <span class="kv-value">{RenderPathList(diff.ChangedPaths)}</span>
                  </div>
                </details>
                """;
    }

    private static string RenderPathList(string[] paths)
    {
        if (paths.Length == 0)
        {
            return "None";
        }

        return string.Join(", ", paths.Select(path => $"<code>{Html(path)}</code>"));
    }

    private static string Html(string value) =>
        WebUtility.HtmlEncode(value);
}
