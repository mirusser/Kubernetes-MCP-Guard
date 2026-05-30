using System.Text.Json;
using Dapper;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.AuditPayloads;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

public sealed class PostgresApprovalPersistence(NpgsqlDataSource dataSource) : IApprovalPersistence
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApprovalPlanResult> CreatePlanAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);

        string canonicalJson = ApprovalCanonicalJson.Serialize(envelope);
        string canonicalHash = ApprovalCanonicalJson.ComputeSha256Hex(canonicalJson);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.plan_envelopes (
                            plan_id,
                            profile,
                            adapter_id,
                            operation,
                            target_namespace,
                            requester_subject,
                            requester_authentication_type,
                            created_at_utc,
                            valid_from_utc,
                            valid_until_utc,
                            policy_kind,
                            operator_group,
                            intent_digest_algorithm,
                            intent_digest_canonicalization,
                            intent_digest_value,
                            review_digest_algorithm,
                            review_digest_canonicalization,
                            review_digest_value,
                            canonical_json_text,
                            canonical_sha256)
                        values (
                            @PlanId,
                            @Profile,
                            @AdapterId,
                            @Operation,
                            @TargetNamespace,
                            @RequesterSubject,
                            @RequesterAuthenticationType,
                            @CreatedAtUtc,
                            @ValidFromUtc,
                            @ValidUntilUtc,
                            @PolicyKind,
                            @OperatorGroup,
                            @IntentDigestAlgorithm,
                            @IntentDigestCanonicalization,
                            @IntentDigestValue,
                            @ReviewDigestAlgorithm,
                            @ReviewDigestCanonicalization,
                            @ReviewDigestValue,
                            @CanonicalJson,
                            @CanonicalHash)
                        """,
                        new
                        {
                            PlanId = envelope.Id,
                            envelope.Profile,
                            envelope.AdapterId,
                            envelope.Operation,
                            TargetNamespace = targetNamespace,
                            RequesterSubject = envelope.Requester.Subject,
                            RequesterAuthenticationType = envelope.Requester.AuthenticationType,
                            envelope.CreatedAtUtc,
                            envelope.ValidFromUtc,
                            envelope.ValidUntilUtc,
                            PolicyKind = envelope.ApprovalPolicy.Type,
                            OperatorGroup = GetPolicyParameter(
                                envelope.ApprovalPolicy,
                                ApprovalConventions.ApprovalPolicyParameters.OperatorGroup),
                            IntentDigestAlgorithm = envelope.IntentDigest.Algorithm,
                            IntentDigestCanonicalization = envelope.IntentDigest.Canonicalization,
                            IntentDigestValue = envelope.IntentDigest.Value,
                            ReviewDigestAlgorithm = envelope.ReviewDigest.Algorithm,
                            ReviewDigestCanonicalization = envelope.ReviewDigest.Canonicalization,
                            ReviewDigestValue = envelope.ReviewDigest.Value,
                            CanonicalJson = canonicalJson,
                            CanonicalHash = canonicalHash
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                    await InsertAuditAsync(
                        connection,
                        transaction,
                        ApprovalConventions.AuditEvents.PlanRequested,
                        new PlanRequestedPayload(
                            envelope.Id,
                            envelope.Operation,
                            targetNamespace,
                            canonicalHash,
                            envelope.IntentDigest,
                            envelope.ReviewDigest),
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }

        return new ApprovalPlanResult(envelope, string.Empty, canonicalHash);
    }

    public async Task<PendingPlanResult> GetPendingPlanAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return PendingPlanResult.Denied(
                "Plan id contains unsupported characters.",
                ApprovalConventions.ResultReasonCodes.InvalidPlanId);
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            if (await IsAppliedAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return PendingPlanResult.Denied(
                    $"Plan '{planId}' was already applied.",
                    ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied);
            }

            var row = await ReadPlanRowAsync(connection, planId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return PendingPlanResult.Denied(
                    $"No pending plan exists for '{planId}'.",
                    ApprovalConventions.ResultReasonCodes.PlanNotPending);
            }

            return DeserializePendingPlan(planId, row);
        }
    }

    public async Task<GrantedPlanResult> GetGrantedPlanAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return GrantedPlanResult.Denied(
                "Plan id contains unsupported characters.",
                grantExists: false,
                reasonCode: ApprovalConventions.ResultReasonCodes.InvalidPlanId);
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var row = await ReadPlanRowAsync(connection, planId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return GrantedPlanResult.MissingGrant(
                    $"No pending plan exists for '{planId}'.",
                    ApprovalConventions.ResultReasonCodes.PlanNotPending);
            }

            if (await IsAppliedAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return GrantedPlanResult.Denied(
                    $"Plan '{planId}' was already applied.",
                    grantExists: false,
                    reasonCode: ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied);
            }

            var grant = await GetGrantAsync(connection, planId, cancellationToken).ConfigureAwait(false);
            if (grant is null)
            {
                return GrantedPlanResult.MissingGrant(
                    $"Plan '{planId}' is not approved yet.",
                    ApprovalConventions.ResultReasonCodes.PlanNotApproved);
            }

            var pending = DeserializePendingPlan(planId, row);
            if (!pending.IsPending || pending.Envelope is null)
            {
                return GrantedPlanResult.Denied(pending.Message, reasonCode: pending.ReasonCode);
            }

            var validation = ApprovalGrantValidation.Validate(pending.Envelope, grant);

            return validation is null
                ? GrantedPlanResult.Granted(pending.Envelope, grant)
                : GrantedPlanResult.Denied(validation.Value.Message, reasonCode: validation.Value.ReasonCode);
        }
    }

    public async Task<PlanStatusResult> GetPlanStatusAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return new PlanStatusResult(PlanStatus.NotFound);
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            if (await IsAppliedAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return new PlanStatusResult(PlanStatus.Applied);
            }

            var grant = await GetGrantAsync(connection, planId, cancellationToken).ConfigureAwait(false);
            if (grant is not null)
            {
                return grant.ExpiresAtUtc <= DateTimeOffset.UtcNow
                    ? new PlanStatusResult(PlanStatus.Expired)
                    : new PlanStatusResult(PlanStatus.Approved);
            }

            bool planExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1
                    from approvals.plan_envelopes
                    where plan_id = @PlanId)
                """,
                new { PlanId = planId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return planExists
                ? new PlanStatusResult(PlanStatus.ApprovalRequired)
                : new PlanStatusResult(PlanStatus.NotFound);
        }
    }

    public async Task<ApprovalChallenge> CreateChallengeAsync(
        string planId,
        string pendingPlanHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingPlanHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterSubject);
        ArgumentNullException.ThrowIfNull(intentDigest);
        ArgumentNullException.ThrowIfNull(reviewDigest);

        var now = DateTimeOffset.UtcNow;
        var challenge = new ApprovalChallenge(
            ApprovalIds.NewChallengeId(),
            planId,
            pendingPlanHash,
            requesterSubject,
            requesterAuthenticationType,
            now,
            now.Add(ttl),
            ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            intentDigest,
            reviewDigest);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.approval_challenges (
                            challenge_id,
                            plan_id,
                            pending_plan_hash,
                            requester_subject,
                            requester_authentication_type,
                            created_at_utc,
                            expires_at_utc,
                            intent_digest_algorithm,
                            intent_digest_canonicalization,
                            intent_digest_value,
                            review_digest_algorithm,
                            review_digest_canonicalization,
                            review_digest_value)
                        values (
                            @ChallengeId,
                            @PlanId,
                            @PendingPlanHash,
                            @RequesterSubject,
                            @RequesterAuthenticationType,
                            @CreatedAtUtc,
                            @ExpiresAtUtc,
                            @IntentDigestAlgorithm,
                            @IntentDigestCanonicalization,
                            @IntentDigestValue,
                            @ReviewDigestAlgorithm,
                            @ReviewDigestCanonicalization,
                            @ReviewDigestValue)
                        """,
                        new
                        {
                            ChallengeId = challenge.Id,
                            challenge.PlanId,
                            challenge.PendingPlanHash,
                            challenge.RequesterSubject,
                            challenge.RequesterAuthenticationType,
                            challenge.CreatedAtUtc,
                            challenge.ExpiresAtUtc,
                            IntentDigestAlgorithm = challenge.IntentDigest.Algorithm,
                            IntentDigestCanonicalization = challenge.IntentDigest.Canonicalization,
                            IntentDigestValue = challenge.IntentDigest.Value,
                            ReviewDigestAlgorithm = challenge.ReviewDigest.Algorithm,
                            ReviewDigestCanonicalization = challenge.ReviewDigest.Canonicalization,
                            ReviewDigestValue = challenge.ReviewDigest.Value
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                    await InsertAuditAsync(
                        connection,
                        transaction,
                        ApprovalConventions.AuditEvents.ApprovalChallengeCreated,
                        new ApprovalChallengeCreatedPayload(
                            challenge.Id,
                            challenge.PlanId,
                            challenge.PendingPlanHash,
                            challenge.RequesterSubject,
                            challenge.RequesterAuthenticationType,
                            challenge.ExpiresAtUtc),
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }

        return challenge;
    }

    public async Task<ApprovalChallenge?> GetChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        if (!IsSafeChallengeId(challengeId))
        {
            return null;
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    """
                    select c.challenge_id,
                           c.plan_id,
                           c.pending_plan_hash,
                           c.requester_subject,
                           c.requester_authentication_type,
                           c.created_at_utc,
                           c.expires_at_utc,
                           c.intent_digest_algorithm,
                           c.intent_digest_canonicalization,
                           c.intent_digest_value,
                           c.review_digest_algorithm,
                           c.review_digest_canonicalization,
                           c.review_digest_value,
                           o.outcome_id,
                           o.status,
                           o.actor_subject,
                           o.decided_at_utc,
                           o.reason,
                           o.grant_id
                    from approvals.approval_challenges c
                    left join approvals.challenge_outcomes o on o.challenge_id = c.challenge_id
                    where c.challenge_id = @ChallengeId
                    """;
                command.Parameters.AddWithValue("ChallengeId", challengeId);

                return await ReadSingleChallengeAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<ApprovalChallenge?> FindPendingChallengeAsync(
        string planId,
        string pendingPlanHash,
        string subject,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intentDigest);
        ArgumentNullException.ThrowIfNull(reviewDigest);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    """
                    select c.challenge_id,
                           c.plan_id,
                           c.pending_plan_hash,
                           c.requester_subject,
                           c.requester_authentication_type,
                           c.created_at_utc,
                           c.expires_at_utc,
                           c.intent_digest_algorithm,
                           c.intent_digest_canonicalization,
                           c.intent_digest_value,
                           c.review_digest_algorithm,
                           c.review_digest_canonicalization,
                           c.review_digest_value,
                           o.outcome_id,
                           o.status,
                           o.actor_subject,
                           o.decided_at_utc,
                           o.reason,
                           o.grant_id
                    from approvals.approval_challenges c
                    left join approvals.challenge_outcomes o on o.challenge_id = c.challenge_id
                    where c.plan_id = @PlanId
                      and c.pending_plan_hash = @PendingPlanHash
                      and c.requester_subject = @RequesterSubject
                      and c.intent_digest_algorithm = @IntentDigestAlgorithm
                      and c.intent_digest_canonicalization = @IntentDigestCanonicalization
                      and c.intent_digest_value = @IntentDigestValue
                      and c.review_digest_algorithm = @ReviewDigestAlgorithm
                      and c.review_digest_canonicalization = @ReviewDigestCanonicalization
                      and c.review_digest_value = @ReviewDigestValue
                      and c.expires_at_utc > @Now
                      and o.outcome_id is null
                    order by c.created_at_utc
                    limit 1
                    """;
                command.Parameters.AddWithValue("PlanId", planId);
                command.Parameters.AddWithValue("PendingPlanHash", pendingPlanHash);
                command.Parameters.AddWithValue("RequesterSubject", subject);
                command.Parameters.AddWithValue("IntentDigestAlgorithm", intentDigest.Algorithm);
                command.Parameters.AddWithValue("IntentDigestCanonicalization", intentDigest.Canonicalization);
                command.Parameters.AddWithValue("IntentDigestValue", intentDigest.Value);
                command.Parameters.AddWithValue("ReviewDigestAlgorithm", reviewDigest.Algorithm);
                command.Parameters.AddWithValue("ReviewDigestCanonicalization", reviewDigest.Canonicalization);
                command.Parameters.AddWithValue("ReviewDigestValue", reviewDigest.Value);
                command.Parameters.AddWithValue("Now", DateTimeOffset.UtcNow);

                return await ReadSingleChallengeAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<ApprovalChallenge> RecordChallengeOutcomeAsync(
        ApprovalChallenge challenge,
        ChallengeOutcome outcome,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(audit);

        var storedOutcome = string.IsNullOrWhiteSpace(outcome.ChallengeId)
            ? outcome with { ChallengeId = challenge.Id }
            : outcome;

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await InsertChallengeOutcomeAsync(connection, transaction, challenge.PlanId, storedOutcome, cancellationToken)
                        .ConfigureAwait(false);
                    await InsertAuditAsync(
                        connection,
                        transaction,
                        audit.EventName,
                        audit.Payload,
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }

        return challenge with
        {
            Status = storedOutcome.Status,
            ApproverSubject = storedOutcome.ActorSubject,
            DecidedAtUtc = storedOutcome.DecidedAtUtc,
            Outcome = storedOutcome
        };
    }

    public async Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await InsertAuditAsync(
                        connection,
                        transaction,
                        audit.EventName,
                        audit.Payload,
                        cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    public async Task<ApprovalGrant> ApproveChallengeAsync(
        ApprovalChallenge challenge,
        PlanEnvelope envelope,
        string approverSubject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverSubject);

        var decidedAtUtc = DateTimeOffset.UtcNow;
        var outcome = new ChallengeOutcome(
            ApprovalIds.NewChallengeOutcomeId(),
            challenge.Id,
            ApprovalConventions.ChallengeOutcomeStatuses.Approved,
            approverSubject,
            decidedAtUtc,
            reason: null,
            grantId: null);
        var grant = new ApprovalGrant(
            ApprovalIds.NewGrantId(),
            envelope.Id,
            envelope.Requester.Subject,
            approverSubject,
            challenge.Id,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            decidedAtUtc,
            envelope.ValidUntilUtc);
        outcome = outcome with { GrantId = grant.Id };

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await InsertChallengeOutcomeAsync(connection, transaction, challenge.PlanId, outcome, cancellationToken)
                        .ConfigureAwait(false);
                    await InsertGrantAsync(connection, transaction, grant, outcome.Id, cancellationToken)
                        .ConfigureAwait(false);

                    await InsertAuditAsync(
                        connection,
                        transaction,
                        ApprovalConventions.AuditEvents.ApprovalChallengeApproved,
                        new ApprovalChallengeApprovedPayload(
                            challenge.Id,
                            challenge.PlanId,
                            challenge.PendingPlanHash,
                            challenge.RequesterSubject,
                            approverSubject,
                            decidedAtUtc),
                        cancellationToken).ConfigureAwait(false);
                    await InsertAuditAsync(
                        connection,
                        transaction,
                        ApprovalConventions.AuditEvents.GrantIssued,
                        new ApprovalGrantIssuedPayload(
                            grant.PlanId,
                            grant.Id,
                            grant.SourceChallengeId,
                            grant.RequesterSubject,
                            grant.ApproverSubject,
                            grant.IntentDigest,
                            grant.ReviewDigest,
                            grant.ExpiresAtUtc),
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }

        return grant;
    }

    public async Task<BeginExecutionAttemptResult> BeginExecutionAttemptAsync(
        string planId,
        ApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(grant);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            if (await IsAppliedAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return BeginExecutionAttemptResult.Refused(
                    $"Plan '{planId}' was already applied.",
                    ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied);
            }

            if (await HasBlockedExecutionOutcomeAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return BeginExecutionAttemptResult.Refused(
                    $"Plan '{planId}' has a terminal blocked execution outcome. Request a new plan.",
                    ApprovalConventions.ResultReasonCodes.ExecutionBlocked);
            }

            if (await HasActiveExecutionClaimAsync(connection, planId, cancellationToken).ConfigureAwait(false))
            {
                return BeginExecutionAttemptResult.Refused(
                    $"Plan '{planId}' already has an active execution claim.",
                    ApprovalConventions.ResultReasonCodes.ExecutionClaimActive);
            }

            var attempt = new ExecutionAttempt(
                ApprovalIds.NewExecutionAttemptId(),
                planId,
                grant.Id,
                DateTimeOffset.UtcNow);

            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.execution_attempts (
                            attempt_id,
                            plan_id,
                            grant_id,
                            started_at_utc)
                        values (
                            @AttemptId,
                            @PlanId,
                            @GrantId,
                            @StartedAtUtc)
                        """,
                        new
                        {
                            AttemptId = attempt.Id,
                            attempt.PlanId,
                            attempt.GrantId,
                            attempt.StartedAtUtc
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.plan_execution_claims (
                            plan_id,
                            attempt_id,
                            claimed_at_utc)
                        values (
                            @PlanId,
                            @AttemptId,
                            @ClaimedAtUtc)
                        """,
                        new
                        {
                            attempt.PlanId,
                            AttemptId = attempt.Id,
                            ClaimedAtUtc = attempt.StartedAtUtc
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (string.Equals(
                           ex.SqlState,
                           PostgresErrorCodes.UniqueViolation,
                           StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return BeginExecutionAttemptResult.Refused(
                        $"Plan '{planId}' already has an active execution claim.",
                        ApprovalConventions.ResultReasonCodes.ExecutionClaimActive);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            return BeginExecutionAttemptResult.Started(attempt);
        }
    }

    public Task RecordExecutionBlockedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken) =>
        RecordExecutionOutcomeAsync(
            attempt,
            ApprovalConventions.ExecutionOutcomeStatuses.Blocked,
            message,
            reasonCode,
            audit,
            targetNamespace: null,
            cancellationToken);

    public Task RecordExecutionFailedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken) =>
        RecordExecutionOutcomeAsync(
            attempt,
            ApprovalConventions.ExecutionOutcomeStatuses.Failed,
            message,
            reasonCode,
            audit,
            targetNamespace: null,
            cancellationToken);

    public async Task RecordExecutionSucceededAsync(
        ExecutionAttempt attempt,
        ApprovalGrant grant,
        string targetNamespace,
        string message,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(audit);

        var outcome = new ExecutionOutcome(
            ApprovalIds.NewExecutionOutcomeId(),
            attempt.Id,
            attempt.PlanId,
            ApprovalConventions.ExecutionOutcomeStatuses.Succeeded,
            DateTimeOffset.UtcNow,
            message,
            ReasonCode: null,
            TargetNamespace: targetNamespace);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await InsertExecutionOutcomeAsync(connection, transaction, outcome, cancellationToken)
                        .ConfigureAwait(false);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into approvals.applied_plans (
                            plan_id,
                            execution_attempt_id,
                            execution_outcome_id,
                            grant_id,
                            target_namespace,
                            applied_at_utc)
                        values (
                            @PlanId,
                            @ExecutionAttemptId,
                            @ExecutionOutcomeId,
                            @GrantId,
                            @TargetNamespace,
                            @AppliedAtUtc)
                        """,
                        new
                        {
                            outcome.PlanId,
                            ExecutionAttemptId = attempt.Id,
                            ExecutionOutcomeId = outcome.Id,
                            GrantId = grant.Id,
                            TargetNamespace = targetNamespace,
                            AppliedAtUtc = outcome.CompletedAtUtc
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    await InsertAuditAsync(
                        connection,
                        transaction,
                        audit.EventName,
                        audit.Payload,
                        cancellationToken).ConfigureAwait(false);
                    await ReleaseExecutionClaimAsync(connection, transaction, attempt.PlanId, cancellationToken)
                        .ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private async Task RecordExecutionOutcomeAsync(
        ExecutionAttempt attempt,
        string status,
        string message,
        string? reasonCode,
        PlanAudit audit,
        string? targetNamespace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(audit);

        var outcome = new ExecutionOutcome(
            ApprovalIds.NewExecutionOutcomeId(),
            attempt.Id,
            attempt.PlanId,
            status,
            DateTimeOffset.UtcNow,
            message,
            reasonCode,
            targetNamespace);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await InsertExecutionOutcomeAsync(connection, transaction, outcome, cancellationToken)
                        .ConfigureAwait(false);
                    await InsertAuditAsync(
                        connection,
                        transaction,
                        audit.EventName,
                        audit.Payload,
                        cancellationToken).ConfigureAwait(false);
                    await ReleaseExecutionClaimAsync(connection, transaction, attempt.PlanId, cancellationToken)
                        .ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private static async Task InsertExecutionOutcomeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into approvals.execution_outcomes (
                outcome_id,
                attempt_id,
                plan_id,
                status,
                completed_at_utc,
                message,
                reason_code,
                target_namespace)
            values (
                @OutcomeId,
                @AttemptId,
                @PlanId,
                @Status,
                @CompletedAtUtc,
                @Message,
                @ReasonCode,
                @TargetNamespace)
            """,
            new
            {
                OutcomeId = outcome.Id,
                outcome.AttemptId,
                outcome.PlanId,
                outcome.Status,
                outcome.CompletedAtUtc,
                outcome.Message,
                outcome.ReasonCode,
                outcome.TargetNamespace
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task InsertGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalGrant grant,
        string sourceOutcomeId,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into approvals.approval_grants (
                grant_id,
                plan_id,
                requester_subject,
                approver_subject,
                source_challenge_id,
                source_outcome_id,
                intent_digest_algorithm,
                intent_digest_canonicalization,
                intent_digest_value,
                review_digest_algorithm,
                review_digest_canonicalization,
                review_digest_value,
                approval_policy_json_text,
                execution_reuse_policy_json_text,
                issued_at_utc,
                expires_at_utc)
            values (
                @GrantId,
                @PlanId,
                @RequesterSubject,
                @ApproverSubject,
                @SourceChallengeId,
                @SourceOutcomeId,
                @IntentDigestAlgorithm,
                @IntentDigestCanonicalization,
                @IntentDigestValue,
                @ReviewDigestAlgorithm,
                @ReviewDigestCanonicalization,
                @ReviewDigestValue,
                @ApprovalPolicyJsonText,
                @ExecutionReusePolicyJsonText,
                @IssuedAtUtc,
                @ExpiresAtUtc)
            """,
            new
            {
                GrantId = grant.Id,
                grant.PlanId,
                grant.RequesterSubject,
                grant.ApproverSubject,
                grant.SourceChallengeId,
                SourceOutcomeId = sourceOutcomeId,
                IntentDigestAlgorithm = grant.IntentDigest.Algorithm,
                IntentDigestCanonicalization = grant.IntentDigest.Canonicalization,
                IntentDigestValue = grant.IntentDigest.Value,
                ReviewDigestAlgorithm = grant.ReviewDigest.Algorithm,
                ReviewDigestCanonicalization = grant.ReviewDigest.Canonicalization,
                ReviewDigestValue = grant.ReviewDigest.Value,
                ApprovalPolicyJsonText = JsonSerializer.Serialize(grant.ApprovalPolicy, jsonOptions),
                ExecutionReusePolicyJsonText = JsonSerializer.Serialize(grant.ExecutionReusePolicy, jsonOptions),
                grant.IssuedAtUtc,
                grant.ExpiresAtUtc
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string? GetPolicyParameter(ApprovalPolicy policy, string parameterName)
    {
        return policy.Parameters is not null &&
               policy.Parameters.TryGetValue(parameterName, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static async Task InsertChallengeOutcomeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string planId,
        ChallengeOutcome outcome,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into approvals.challenge_outcomes (
                outcome_id,
                challenge_id,
                plan_id,
                status,
                actor_subject,
                decided_at_utc,
                reason,
                grant_id)
            values (
                @OutcomeId,
                @ChallengeId,
                @PlanId,
                @Status,
                @ActorSubject,
                @DecidedAtUtc,
                @Reason,
                @GrantId)
            """,
            new
            {
                OutcomeId = outcome.Id,
                outcome.ChallengeId,
                PlanId = planId,
                outcome.Status,
                outcome.ActorSubject,
                outcome.DecidedAtUtc,
                outcome.Reason,
                outcome.GrantId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var correlation = AuditCorrelation.FromPayload(payload);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into approvals.audit_events (
                event_name,
                plan_id,
                challenge_id,
                grant_id,
                execution_attempt_id,
                occurred_at_utc,
                payload_json_text)
            values (
                @EventName,
                @PlanId,
                @ChallengeId,
                @GrantId,
                @ExecutionAttemptId,
                @OccurredAtUtc,
                @PayloadJsonText)
            """,
            new
            {
                EventName = eventName,
                correlation.PlanId,
                correlation.ChallengeId,
                correlation.GrantId,
                correlation.ExecutionAttemptId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                PayloadJsonText = JsonSerializer.Serialize(payload, jsonOptions)
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task ReleaseExecutionClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string planId,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from approvals.plan_execution_claims
            where plan_id = @PlanId
            """,
            new { PlanId = planId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<PlanRow?> ReadPlanRowAsync(
        NpgsqlConnection connection,
        string planId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                """
                select canonical_json_text,
                       canonical_sha256
                from approvals.plan_envelopes
                where plan_id = @PlanId
                """;
            command.Parameters.AddWithValue("PlanId", planId);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return new PlanRow(reader.GetString(0), reader.GetString(1));
            }
        }
    }

    private static async Task<ApprovalChallenge?> ReadSingleChallengeAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            string challengeId = reader.GetString(0);
            string planId = reader.GetString(1);
            string pendingPlanHash = reader.GetString(2);
            string requesterSubject = reader.GetString(3);
            string? requesterAuthenticationType = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(4);
            DateTimeOffset createdAtUtc = await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset expiresAtUtc = await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
                .ConfigureAwait(false);
            var intentDigest = new ApprovalDigest(reader.GetString(7), reader.GetString(8), reader.GetString(9));
            var reviewDigest = new ApprovalDigest(reader.GetString(10), reader.GetString(11), reader.GetString(12));
            ChallengeOutcome? outcome = null;
            string status = ApprovalConventions.ChallengeStatuses.Pending;
            string? actorSubject = null;
            DateTimeOffset? decidedAtUtc = null;
            if (!await reader.IsDBNullAsync(13, cancellationToken).ConfigureAwait(false))
            {
                string outcomeId = reader.GetString(13);
                status = reader.GetString(14);
                actorSubject = await reader.IsDBNullAsync(15, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(15);
                decidedAtUtc = await reader.GetFieldValueAsync<DateTimeOffset>(16, cancellationToken)
                    .ConfigureAwait(false);
                outcome = new ChallengeOutcome(
                    outcomeId,
                    challengeId,
                    status,
                    actorSubject,
                    decidedAtUtc.Value,
                    await reader.IsDBNullAsync(17, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(17),
                    await reader.IsDBNullAsync(18, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(18));
            }

            return new ApprovalChallenge(
                challengeId,
                planId,
                pendingPlanHash,
                requesterSubject,
                requesterAuthenticationType,
                createdAtUtc,
                expiresAtUtc,
                status,
                actorSubject,
                decidedAtUtc,
                intentDigest,
                reviewDigest,
                outcome);
        }
    }

    private static async Task<ApprovalGrant?> GetGrantAsync(
        NpgsqlConnection connection,
        string planId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                """
                select grant_id,
                       plan_id,
                       requester_subject,
                       approver_subject,
                       source_challenge_id,
                       intent_digest_algorithm,
                       intent_digest_canonicalization,
                       intent_digest_value,
                       review_digest_algorithm,
                       review_digest_canonicalization,
                       review_digest_value,
                       approval_policy_json_text,
                       execution_reuse_policy_json_text,
                       issued_at_utc,
                       expires_at_utc
                from approvals.approval_grants
                where plan_id = @PlanId
                """;
            command.Parameters.AddWithValue("PlanId", planId);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return new ApprovalGrant(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    new ApprovalDigest(reader.GetString(5), reader.GetString(6), reader.GetString(7)),
                    new ApprovalDigest(reader.GetString(8), reader.GetString(9), reader.GetString(10)),
                    JsonSerializer.Deserialize<ApprovalPolicy>(reader.GetString(11), jsonOptions) ?? new ApprovalPolicy(),
                    JsonSerializer.Deserialize<ExecutionReusePolicy>(reader.GetString(12), jsonOptions) ?? new ExecutionReusePolicy(),
                    await reader.GetFieldValueAsync<DateTimeOffset>(13, cancellationToken).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<DateTimeOffset>(14, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        string planId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from approvals.applied_plans
                where plan_id = @PlanId)
            """,
            new { PlanId = planId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task<bool> HasActiveExecutionClaimAsync(
        NpgsqlConnection connection,
        string planId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from approvals.plan_execution_claims
                where plan_id = @PlanId)
            """,
            new { PlanId = planId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task<bool> HasBlockedExecutionOutcomeAsync(
        NpgsqlConnection connection,
        string planId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from approvals.execution_outcomes
                where plan_id = @PlanId
                  and status = @Status)
            """,
            new
            {
                PlanId = planId,
                Status = ApprovalConventions.ExecutionOutcomeStatuses.Blocked
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static PendingPlanResult DeserializePendingPlan(string planId, PlanRow row)
    {
        var envelope = JsonSerializer.Deserialize<PlanEnvelope>(row.CanonicalJsonText, jsonOptions);
        if (envelope is null || !string.Equals(envelope.Id, planId, StringComparison.Ordinal))
        {
            return PendingPlanResult.Denied(
                $"Plan '{planId}' could not be read.",
                ApprovalConventions.ResultReasonCodes.PlanReadFailed);
        }

        return PendingPlanResult.Found(envelope, string.Empty, row.CanonicalSha256);
    }


    private static bool IsSafePlanId(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return false;
        }

        return planId.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-');
    }

    private static bool IsSafeChallengeId(string challengeId)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            return false;
        }

        return challengeId.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'));
    }

    private sealed record class AuditCorrelation(
        string? PlanId,
        string? ChallengeId,
        string? GrantId,
        string? ExecutionAttemptId)
    {
        public static AuditCorrelation FromPayload(object payload) =>
            payload switch
            {
                IChallengeAuditPayload challenge => new(
                    challenge.PlanId,
                    challenge.Id,
                    GrantId: null,
                    ExecutionAttemptId: null),
                ApprovalGrantIssuedPayload grant => new(
                    grant.PlanId,
                    ChallengeId: grant.SourceChallengeId,
                    GrantId: grant.GrantId,
                    ExecutionAttemptId: null),
                IPlanAuditPayload plan => new(
                    plan.PlanId,
                    ChallengeId: null,
                    GrantId: null,
                    ExecutionAttemptId: null),
                _ => new(null, null, null, null)
            };
    }

    private sealed record class PlanRow(string CanonicalJsonText, string CanonicalSha256);
}
