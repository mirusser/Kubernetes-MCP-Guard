using System.Diagnostics.Metrics;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Mcp;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class BatchProcessorTests
{
    [Fact]
    public async Task ProcessBatchAsync_ValidDeploymentDecision_ProposesPlanAndPublishesProposal()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          },
          "reasoning": "Deployment has no available replicas."
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"Content":[{"Text":"{\"planId\":\"plan-123\",\"accessCodeSent\":true,\"codeExpiresAt\":\"2026-05-25T12:00:00Z\"}"}]}""");
        var sink = new CapturingRemediationProposalSink();

        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Is<IReadOnlyDictionary<string, object?>?>(args => MatchesProposeArguments(args)),
            Arg.Any<CancellationToken>());

        var published = Assert.Single(sink.Batches);
        Assert.Equal(batch.CycleId, published.CycleId);
        var proposal = Assert.Single(published.Proposals);
        Assert.Equal("plan-123", proposal.PlanId);
        Assert.Equal(batch.Reports[0].AnomalyId, proposal.AnomalyId);
    }

    [Fact]
    public async Task ProcessBatchAsync_SystemPrompt_StatesPlannerServiceCallsProposePlan()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var capturedMessages = new List<ChatMessage>();
        var chatClient = new FixtureChatClient(messages =>
        {
            capturedMessages.AddRange(messages);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {
              "operationType": "restart_deployment",
              "arguments": {
                "name": "nginx-demo",
                "namespace": "mcp-nginx-demo"
              }
            }
            """));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-123"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        var systemMessage = Assert.Single(capturedMessages, message => message.Role == ChatRole.System);
        string systemText = string.Concat(systemMessage.Contents
            .OfType<TextContent>()
            .Select(content => content.Text));

        Assert.DoesNotContain("never call propose_plan", systemText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Planner service can validate it and call propose_plan", systemText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessBatchAsync_UnsupportedOperation_DoesNotProposeAndRecordsInvalidOperation()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.DecisionInvalidOperationCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "delete_resource",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_InvalidArguments_DoesNotProposeAndRecordsInvalidArguments()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.DecisionInvalidArgumentsCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "scale_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo",
            "replicas": -1
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_DecisionTimeout_DoesNotProposeAndRecordsTimeout()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.DecisionTimeoutCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient(async (_, cancellationToken) =>
        {
            // Simulate a timed-out LLM call by throwing OperationCanceledException.
            throw new OperationCanceledException(cancellationToken);
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink,
            new PlannerOptions
            {
                GatewayBaseUrl = "http://localhost:3001/mcp",
                LlmApiKey = "test-key",
                AnomalyWallClockCapSeconds = 30,
                BatchWallClockCapSeconds = 300,
                MaxToolIterations = 4,
            },
            meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_ProposePlanFails_DoesNotPublishAndRecordsFailure()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.ProposeFailedCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new HttpRequestException("gateway unavailable")));
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_DuplicateActiveAnomaly_ProposesOnce()
    {
        var report = CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo");
        var batch = CreateBatch(report, report);
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-123"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        var published = Assert.Single(sink.Batches);
        Assert.Single(published.Proposals);
    }

    [Fact]
    public async Task ProcessBatchAsync_ResolvedAnomaly_ClearsDedupeState()
    {
        var activeReport = CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo");
        var resolvedReport = activeReport with { Status = AnomalyStatus.Resolved };
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-1"}""", """{"planId":"plan-2"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(CreateBatch(activeReport), CancellationToken.None);
        await processor.ProcessBatchAsync(CreateBatch(activeReport), CancellationToken.None);
        await processor.ProcessBatchAsync(CreateBatch(resolvedReport), CancellationToken.None);
        await processor.ProcessBatchAsync(CreateBatch(activeReport), CancellationToken.None);

        await mcpClient.Received(2).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(2, sink.Batches.Count);
        Assert.Equal("plan-1", sink.Batches[0].Proposals[0].PlanId);
        Assert.Equal("plan-2", sink.Batches[1].Proposals[0].PlanId);
    }

    [Fact]
    public async Task ProcessBatchAsync_ReadOnlyToolCallBeforeDecision_CallsInspectionToolThenProposes()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        int callCount = 0;
        var chatClient = new FixtureChatClient(_ =>
        {
            callCount++;
            return callCount == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "TOOL_CALL: {\"tool\":\"get_k8s_status\",\"arguments\":{\"namespace\":\"mcp-nginx-demo\"}}"))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                  {
                    "operationType": "restart_deployment",
                    "arguments": {
                      "name": "nginx-demo",
                      "namespace": "mcp-nginx-demo"
                    }
                  }
                  """));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var toolName = callInfo.ArgAt<string>(0);
                return toolName == PlannerConventions.ToolNames.ProposePlan
                    ? """{"planId":"plan-123"}"""
                    : "{}";
            });
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.GetK8sStatus,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Single(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_DoesNotProposeOrPublish()
    {
        var batch = CreateBatch();
        var chatClient = new FixtureChatClient("");
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_UnsupportedAnomalyKind_FiltersAndSkips()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            (AnomalyKind)999,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("{}");
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_MixedStatusBatch_OnlyProcessesActive()
    {
        var activeReport = CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo");
        var resolvedReport = activeReport with { Status = AnomalyStatus.Resolved };
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-1"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(CreateBatch(resolvedReport, activeReport), CancellationToken.None);

        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        var published = Assert.Single(sink.Batches);
        Assert.Single(published.Proposals);
    }

    [Fact]
    public async Task ProcessBatchAsync_MultiAnomalyBatch_ProposesBoth()
    {
        var anomaly1 = CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo");
        var anomaly2 = anomaly1 with
        {
            AnomalyId = "anomaly-456",
            Target = anomaly1.Target with { Name = "other-deployment" },
        };
        var batch = CreateBatch(anomaly1, anomaly2);
        int callIndex = 0;
        var chatClient = new FixtureChatClient(messages =>
        {
            int index = Interlocked.Increment(ref callIndex);
            string name = index == 1 ? "nginx-demo" : "other-deployment";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""
            {
              "operationType": "restart_deployment",
              "arguments": {
                "name": "{{name}}",
                "namespace": "mcp-nginx-demo"
              }
            }
            """));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-1"}""", """{"planId":"plan-2"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.Received(2).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        var published = Assert.Single(sink.Batches);
        Assert.Equal(2, published.Proposals.Count);
    }

    [Fact]
    public async Task ProcessBatchAsync_MissingPlanIdResponse_RecordsProposeFailed()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.ProposeFailedCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"ok"}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_LlmReturnsEmptyResponse_DoesNotProposeNoCounter()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("");
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task ProcessBatchAsync_LlmReturnsUnparseableResponse_RecordsInvalidArguments(string llmResponse)
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.DecisionInvalidArgumentsCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient(llmResponse);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
    }

    [Fact]
    public async Task ProcessBatchAsync_NonReadOnlyToolCall_RejectsAndContinues()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        int callCount = 0;
        var chatClient = new FixtureChatClient(_ =>
        {
            callCount++;
            return callCount == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "TOOL_CALL: {\"tool\":\"propose_plan\",\"arguments\":{\"operationType\":\"restart_deployment\"}}"))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                  {
                    "operationType": "restart_deployment",
                    "arguments": {
                      "name": "nginx-demo",
                      "namespace": "mcp-nginx-demo"
                    }
                  }
                  """));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var toolName = callInfo.ArgAt<string>(0);
                return toolName == PlannerConventions.ToolNames.ProposePlan
                    ? """{"planId":"plan-123"}"""
                    : "{}";
            });
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        // The non-readonly tool call should be rejected; propose_plan should still succeed on the decision.
        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Single(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_ToolIterationExhausted_StopsAfterMaxIterations()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        int callCount = 0;
        var chatClient = new FixtureChatClient(_ =>
        {
            callCount++;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "TOOL_CALL: {\"tool\":\"get_k8s_status\",\"arguments\":{}}"));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("{}");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink,
            new PlannerOptions
            {
                GatewayBaseUrl = "http://localhost:3001/mcp",
                LlmApiKey = "test-key",
                AnomalyWallClockCapSeconds = 30,
                BatchWallClockCapSeconds = 300,
                MaxToolIterations = 2,
            });

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        // LLM kept returning tool calls; after MaxToolIterations=2 it should stop.
        await mcpClient.Received(2).CallToolAsync(
            PlannerConventions.ToolNames.GetK8sStatus,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_ShutdownCancellation_ThrowsOperationCanceledException()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("{}");
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            processor.ProcessBatchAsync(batch, cts.Token));
    }

    [Fact]
    public async Task ProcessBatchAsync_MultipleToolCallsThenDecision_CallsToolsAndProposes()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        int callCount = 0;
        var chatClient = new FixtureChatClient(_ =>
        {
            callCount++;
            return callCount <= 2
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "TOOL_CALL: {\"tool\":\"get_k8s_status\",\"arguments\":{\"namespace\":\"mcp-nginx-demo\"}}"))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                  {
                    "operationType": "restart_deployment",
                    "arguments": {
                      "name": "nginx-demo",
                      "namespace": "mcp-nginx-demo"
                    }
                  }
                  """));
        });
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var toolName = callInfo.ArgAt<string>(0);
                return toolName == PlannerConventions.ToolNames.ProposePlan
                    ? """{"planId":"plan-123"}"""
                    : "{}";
            });
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        await mcpClient.Received(2).CallToolAsync(
            PlannerConventions.ToolNames.GetK8sStatus,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        await mcpClient.Received(1).CallToolAsync(
            PlannerConventions.ToolNames.ProposePlan,
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
        Assert.Single(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_ScaleDeployment_NegativeReplicas_RecordsInvalidArguments()
    {
        using var meter = new Meter("planner-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.DecisionInvalidArgumentsCounterName);
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "scale_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo",
            "replicas": -1
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink, meter: meter);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Empty(sink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task ProcessBatchAsync_NestedPlanIdResponse_ExtractsPlanId()
    {
        var batch = CreateBatch(CreateAnomaly(
            AnomalyStatus.Active,
            AnomalyKind.DeploymentUnavailable,
            "Deployment",
            "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "nginx-demo",
            "namespace": "mcp-nginx-demo"
          }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"content":[{"text":"{\"planId\":\"plan-x\"}"}]}""");
        var sink = new CapturingRemediationProposalSink();
        var processor = CreateProcessor(chatClient, mcpClient, sink);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        var published = Assert.Single(sink.Batches);
        var proposal = Assert.Single(published.Proposals);
        Assert.Equal("plan-x", proposal.PlanId);
    }

    private static BatchProcessor CreateProcessor(
        FixtureChatClient chatClient,
        IPlannerMcpClient mcpClient,
        IRemediationProposalSink sink,
        PlannerOptions? options = null,
        Meter? meter = null,
        ILogger<BatchProcessor>? logger = null)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<PlannerOptions>>();
        optionsMonitor.CurrentValue.Returns(options ?? new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            AnomalyWallClockCapSeconds = 30,
            BatchWallClockCapSeconds = 300,
            MaxToolIterations = 4,
        });

        return new BatchProcessor(
            optionsMonitor,
            new AnomalyBatchQueue(),
            chatClient,
            mcpClient,
            sink,
            logger ?? NullLogger<BatchProcessor>.Instance,
            meter: meter);
    }

    private static CounterProbe ListenForCounter(Meter meter, string counterName)
    {
        return new CounterProbe(meter, counterName);
    }

    private sealed class CounterProbe : IDisposable
    {
        private readonly MeterListener listener;

        public CounterProbe(Meter meter, string counterName)
        {
            listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter && instrument.Name == counterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, state) =>
                {
                    Measurements.Add(new Measurement<long>(measurement, tags));
                });
            listener.Start();
        }

        public List<Measurement<long>> Measurements { get; } = [];

        public void Dispose()
        {
            listener.Dispose();
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_ResolvedAnomaly_LogsFilterDroppedResolved()
    {
        var activeReport = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable, "Deployment", "nginx-demo");
        var resolvedReport = activeReport with { Status = AnomalyStatus.Resolved };
        var logger = new CapturingLogger<BatchProcessor>();
        var processor = CreateProcessor(
            new FixtureChatClient(""),
            Substitute.For<IPlannerMcpClient>(),
            new CapturingRemediationProposalSink(),
            logger: logger);

        await processor.ProcessBatchAsync(CreateBatch(resolvedReport), CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Properties.TryGetValue("Reason", out var r) &&
            PlannerConventions.FilterDropReasons.Resolved.Equals(r) &&
            e.Properties.TryGetValue("AnomalyId", out var id) &&
            resolvedReport.AnomalyId.Equals(id));
    }

    [Fact]
    public async Task ProcessBatchAsync_UnsupportedAnomalyKind_LogsFilterDroppedUnsupportedKind()
    {
        var unsupportedReport = CreateAnomaly(AnomalyStatus.Active, (AnomalyKind)999, "Deployment", "nginx-demo");
        var logger = new CapturingLogger<BatchProcessor>();
        var processor = CreateProcessor(
            new FixtureChatClient(""),
            Substitute.For<IPlannerMcpClient>(),
            new CapturingRemediationProposalSink(),
            logger: logger);

        await processor.ProcessBatchAsync(CreateBatch(unsupportedReport), CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Properties.TryGetValue("Reason", out var r) &&
            PlannerConventions.FilterDropReasons.UnsupportedKind.Equals(r));
    }

    [Fact]
    public async Task ProcessBatchAsync_ValidDecision_LogsDecisionCompleted()
    {
        var batch = CreateBatch(CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable, "Deployment", "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-123"}""");
        var logger = new CapturingLogger<BatchProcessor>();
        var processor = CreateProcessor(chatClient, mcpClient, new CapturingRemediationProposalSink(), logger: logger);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Properties.TryGetValue("OperationType", out var op) &&
            PlannerConventions.OperationTypes.RestartDeployment.Equals(op) &&
            e.Properties.ContainsKey("AnomalyId"));
    }

    [Fact]
    public async Task ProcessBatchAsync_SuccessfulPropose_LogsProposePlanSucceeded()
    {
        var batch = CreateBatch(CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable, "Deployment", "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-999"}""");
        var logger = new CapturingLogger<BatchProcessor>();
        var processor = CreateProcessor(chatClient, mcpClient, new CapturingRemediationProposalSink(), logger: logger);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Properties.TryGetValue("PlanId", out var pid) && "plan-999".Equals(pid) &&
            e.Properties.ContainsKey("AnomalyId"));
    }

    [Fact]
    public async Task ProcessBatchAsync_ValidProposal_LogsHandoffPublished()
    {
        var batch = CreateBatch(CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable, "Deployment", "nginx-demo"));
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
        }
        """);
        var mcpClient = Substitute.For<IPlannerMcpClient>();
        mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"planId":"plan-pub"}""");
        var logger = new CapturingLogger<BatchProcessor>();
        var processor = CreateProcessor(chatClient, mcpClient, new CapturingRemediationProposalSink(), logger: logger);

        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Properties.TryGetValue("CycleId", out var c) && batch.CycleId.Equals(c) &&
            e.Properties.TryGetValue("ProposalCount", out var n) && 1.Equals(n));
    }

    private static bool MatchesProposeArguments(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null ||
            (string)args[PlannerConventions.ToolArguments.OperationType]! != PlannerConventions.OperationTypes.RestartDeployment ||
            args[PlannerConventions.ToolArguments.OperationArguments] is not IReadOnlyDictionary<string, object?> operationArgs)
        {
            return false;
        }

        return (string)operationArgs[PlannerConventions.ToolArguments.Name]! == "nginx-demo" &&
            (string)operationArgs[PlannerConventions.ToolArguments.Namespace]! == "mcp-nginx-demo";
    }

    private static AnomalyHandoffBatch CreateBatch(params AnomalyReport[] reports)
    {
        return new AnomalyHandoffBatch
        {
            CycleId = "cycle-1",
            EmittedAt = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero),
            Reports = reports,
        };
    }

    private static AnomalyReport CreateAnomaly(
        AnomalyStatus status,
        AnomalyKind kind,
        string resourceKind,
        string name)
    {
        return new AnomalyReport
        {
            AnomalyId = "anomaly-123",
            CycleId = "cycle-1",
            DetectedAt = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero),
            Kind = kind,
            Target = new ResourceRef
            {
                ApiVersion = resourceKind == "Deployment" ? "apps/v1" : "v1",
                Kind = resourceKind,
                Namespace = "mcp-nginx-demo",
                Name = name,
            },
            Severity = Severity.High,
            Status = status,
            Summary = "Deployment has no available replicas.",
            Evidence = [],
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private sealed class CapturingRemediationProposalSink : IRemediationProposalSink
    {
        public List<RemediationProposalBatch> Batches { get; } = [];

        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }
}
