using System.Globalization;
using System.Text.Json;

namespace InfraGate.Remediation.E2E.Tests;

/// <summary>
/// A parsed InfraGate approval email, per the deterministic plaintext body built by
/// ApprovalEmailRenderer.RenderPlaintext: only PlanId/OperationSummary/AccessCode/ApprovalUrl/
/// ExpiresAtUtc are structural (Gateway-authored); nothing LLM-authored is exposed here.
/// </summary>
public sealed record class ApprovalEmail(
    string PlanId,
    string OperationSummary,
    string AccessCode,
    string ApprovalUrl,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Talks to a live Mailpit instance's v1 REST API to find and parse approval emails.</summary>
public sealed class MailpitClient(Uri baseUri) : IDisposable
{
    private const string ApprovalSubjectPrefix = "InfraGate approval requested";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly HttpClient http = new() { BaseAddress = baseUri };

    /// <summary>
    /// Polls Mailpit until it sees an InfraGate approval email created at or after
    /// <paramref name="since"/> (there is no plan id to search by up front — the plan is created
    /// by the live agentic loop only after the request that triggers this poll), then parses it.
    /// </summary>
    public async Task<ApprovalEmail> FindLatestApprovalEmailAsync(
        DateTimeOffset since,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (true)
        {
            string? messageId = await FindMessageIdAsync(since, linkedCts.Token).ConfigureAwait(false);
            if (messageId is not null)
            {
                string body = await GetMessageTextAsync(messageId, linkedCts.Token).ConfigureAwait(false);
                return ParseApprovalEmail(body);
            }

            try
            {
                await Task.Delay(PollInterval, TimeProvider.System, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No approval email created after {since:O} arrived in Mailpit within {timeout}.");
            }
        }
    }

    private async Task<string?> FindMessageIdAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await http.GetAsync("/api/v1/messages", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using System.IO.Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document =
            await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("messages", out JsonElement messages))
        {
            return null;
        }

        foreach (JsonElement message in messages.EnumerateArray())
        {
            string? subject = message.TryGetProperty("Subject", out JsonElement subjectProperty)
                ? subjectProperty.GetString()
                : null;

            if (subject is null || !subject.StartsWith(ApprovalSubjectPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (message.TryGetProperty("Created", out JsonElement createdProperty)
                && createdProperty.GetString() is { } createdRaw
                && DateTimeOffset.TryParse(
                    createdRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset created)
                && created < since)
            {
                continue;
            }

            return message.GetProperty("ID").GetString();
        }

        return null;
    }

    private async Task<string> GetMessageTextAsync(string messageId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await http.GetAsync($"/api/v1/message/{messageId}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using System.IO.Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document =
            await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return document.RootElement.GetProperty("Text").GetString()
            ?? throw new InvalidOperationException($"Mailpit message '{messageId}' had no plaintext body.");
    }

    private static ApprovalEmail ParseApprovalEmail(string body)
    {
        string planId = ExtractLine(body, "Plan: ")
            ?? throw new InvalidOperationException("Approval email did not contain a 'Plan:' line.");
        string summary = ExtractLine(body, "Summary: ")
            ?? throw new InvalidOperationException("Approval email did not contain a 'Summary:' line.");
        string accessCode = ExtractLine(body, "Code: ")
            ?? throw new InvalidOperationException("Approval email did not contain a 'Code:' line.");
        string approvalUrl = ExtractLine(body, "Review: ")
            ?? throw new InvalidOperationException("Approval email did not contain a 'Review:' line.");
        string expiresRaw = ExtractLine(body, "Expires: ")
            ?? throw new InvalidOperationException("Approval email did not contain an 'Expires:' line.");

        return new ApprovalEmail(
            planId,
            summary,
            accessCode,
            approvalUrl,
            DateTimeOffset.Parse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static string? ExtractLine(string body, string prefix)
    {
        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    public void Dispose() => http.Dispose();
}
