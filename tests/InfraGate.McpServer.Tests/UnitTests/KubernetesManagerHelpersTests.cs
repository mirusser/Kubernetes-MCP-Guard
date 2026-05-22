using System.Net;
using System.Net.Http;
using InfraGate.McpServer;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesManagerHelpersTests
{
    [Fact]
    public void IsConflict_KubernetesExceptionWithConflictCode_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 409 };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_KubernetesExceptionWithConflictReason_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 500, Reason = "Conflict" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_HttpOperationExceptionWithConflictStatus_ReturnsTrue()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.Conflict);

        var result = KubernetesManagerHelpers.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_KubernetesExceptionWithoutConflict_ReturnsFalse()
    {
        var status = new k8s.Models.V1Status { Code = 404, Reason = "NotFound" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.IsConflict(ex);

        Assert.False(result);
    }

    [Fact]
    public void IsConflict_NonMatchingException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("test");

        var result = KubernetesManagerHelpers.IsConflict(ex);

        Assert.False(result);
    }

    [Fact]
    public void FormatServerSideApplyException_WithConflict_IncludesApplyConflictMessage()
    {
        var status = new k8s.Models.V1Status { Code = 409, Reason = "Conflict", Message = "field is immutable" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.FormatServerSideApplyException("Apply failed", ex);

        Assert.Contains("Apply refused by Kubernetes field ownership conflict", result);
        Assert.Contains("field is immutable", result);
    }

    [Fact]
    public void FormatServerSideApplyException_WithoutConflict_OmitsConflictMessage()
    {
        var status = new k8s.Models.V1Status { Code = 404, Reason = "NotFound", Message = "not found" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.FormatServerSideApplyException("Apply failed", ex);

        Assert.DoesNotContain("field ownership conflict", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public void TryFormatKubernetesException_WithStatus_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 500, Reason = "InternalError", Message = "server error" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.TryFormatKubernetesException("prefix", ex, out var message);

        Assert.True(result);
        Assert.Contains("500 InternalError: server error", message);
    }

    [Fact]
    public void TryFormatKubernetesException_WithNullStatus_ReturnsFalse()
    {
        var ex = new KubernetesException();

        var result = KubernetesManagerHelpers.TryFormatKubernetesException("prefix", ex, out var message);

        Assert.False(result);
        Assert.Empty(message);
    }

    [Fact]
    public void TryFormatHttpOperationException_WithResponse_ReturnsTrue()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.InternalServerError, "Server Error", "request failed");

        var result = KubernetesManagerHelpers.TryFormatHttpOperationException("prefix", ex, out var message);

        Assert.True(result);
        Assert.Contains("500 Server Error", message);
    }

    [Fact]
    public void TryFormatHttpOperationException_WithNullResponse_ReturnsFalse()
    {
        var ex = new HttpOperationException("generic error");

        var result = KubernetesManagerHelpers.TryFormatHttpOperationException("prefix", ex, out var message);

        Assert.False(result);
        Assert.Empty(message);
    }

    [Fact]
    public void FormatApiException_NonKubernetesNonHttpOperationException_ReturnsFallbackMessage()
    {
        var ex = new ArgumentException("invalid argument");

        var result = KubernetesManagerHelpers.FormatApiException("prefix", ex);

        Assert.Equal("prefix: invalid argument", result);
    }

    [Fact]
    public void FormatApiException_HttpOperationException_ReturnsHttpFormatMessage()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.BadRequest, "Bad Request", "invalid input");

        var result = KubernetesManagerHelpers.FormatApiException("prefix", ex);

        Assert.Contains("400 Bad Request", result);
    }

    [Fact]
    public void FormatApiException_KubernetesException_ReturnsKubernetesFormatMessage()
    {
        var status = new k8s.Models.V1Status { Code = 422, Reason = "UnprocessableEntity", Message = "validation failed" };
        var ex = new KubernetesException(status);

        var result = KubernetesManagerHelpers.FormatApiException("prefix", ex);

        Assert.Contains("422 UnprocessableEntity: validation failed", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNamespace_NullOrWhitespace_ReturnsRequiredMessage(string? namespaceName)
    {
        var options = new KubernetesMcpOptions(
            AllowedNamespaces: new HashSet<string>(["default"]),
            ApprovalRoot: "/tmp/approvals");

        var result = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName!);

        Assert.Equal("Namespace is required.", result);
    }

    [Fact]
    public void ValidateNamespace_DisallowedNamespace_ReturnsNotAllowedMessage()
    {
        var options = new KubernetesMcpOptions(
            AllowedNamespaces: new HashSet<string>(["production"]),
            ApprovalRoot: "/tmp/approvals");

        var result = KubernetesManagerHelpers.ValidateNamespace(options, "staging");

        Assert.NotNull(result);
        Assert.Contains("staging", result);
        Assert.Contains("not allowed", result);
    }

    [Fact]
    public void ValidateNamespace_AllowedNamespace_ReturnsNull()
    {
        var options = new KubernetesMcpOptions(
            AllowedNamespaces: new HashSet<string>(["production"]),
            ApprovalRoot: "/tmp/approvals");

        var result = KubernetesManagerHelpers.ValidateNamespace(options, "production");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidateName_NullOrWhitespace_ReturnsRequiredMessage(string? name)
    {
        var result = KubernetesManagerHelpers.ValidateName(name!);

        Assert.Equal("Resource name is required.", result);
    }

    [Fact]
    public void ValidateName_ValidName_ReturnsNull()
    {
        var result = KubernetesManagerHelpers.ValidateName("my-deployment");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null, "Image")]
    [InlineData("", "Image")]
    [InlineData("  ", "Tag")]
    public void ValidateRequiredText_NullOrWhitespace_ReturnsRequiredMessage(string? value, string fieldName)
    {
        var result = KubernetesManagerHelpers.ValidateRequiredText(value!, fieldName);

        Assert.Equal($"{fieldName} is required.", result);
    }

    [Fact]
    public void ValidateRequiredText_ValidValue_ReturnsNull()
    {
        var result = KubernetesManagerHelpers.ValidateRequiredText("nginx:latest", "Image");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(KubernetesManagerHelpers.MaxReplicas + 1)]
    public void ValidateReplicas_OutOfRange_ReturnsErrorMessage(int replicas)
    {
        var result = KubernetesManagerHelpers.ValidateReplicas(replicas);

        Assert.NotNull(result);
        Assert.Contains("Replicas must be between", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(KubernetesManagerHelpers.MaxReplicas)]
    public void ValidateReplicas_ValidRange_ReturnsNull(int replicas)
    {
        var result = KubernetesManagerHelpers.ValidateReplicas(replicas);

        Assert.Null(result);
    }

    [Fact]
    public void IsNotFound_KubernetesException404_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 404 };
        var ex = new KubernetesException(status);

        Assert.True(KubernetesManagerHelpers.IsNotFound(ex));
    }

    [Fact]
    public void IsNotFound_HttpOperationException404_ReturnsTrue()
    {
        var ex = CreateHttpOperationException(System.Net.HttpStatusCode.NotFound);

        Assert.True(KubernetesManagerHelpers.IsNotFound(ex));
    }

    [Fact]
    public void IsNotFound_KubernetesException500_ReturnsFalse()
    {
        var status = new k8s.Models.V1Status { Code = 500 };
        var ex = new KubernetesException(status);

        Assert.False(KubernetesManagerHelpers.IsNotFound(ex));
    }

    [Fact]
    public void IsNotFound_OtherException_ReturnsFalse()
    {
        Assert.False(KubernetesManagerHelpers.IsNotFound(new InvalidOperationException("oops")));
    }

    [Fact]
    public void FormatObjectRef_ReturnsApiVersionKindNamespaceSlashName()
    {
        var obj = new InfraGate.KubernetesAdapter.KubernetesObjectRef("apps/v1", "Deployment", "production", "web-api");

        var result = KubernetesManagerHelpers.FormatObjectRef(obj);

        Assert.Equal("apps/v1 Deployment production/web-api", result);
    }

    private static HttpOperationException CreateHttpOperationException(
        HttpStatusCode statusCode,
        string reasonPhrase = "Reason",
        string message = "error message")
    {
        var httpResponse = new HttpResponseMessage(statusCode) { ReasonPhrase = reasonPhrase };
        var wrapper = new HttpResponseMessageWrapper(httpResponse, "body");
        var ex = new HttpOperationException(message);
        typeof(HttpOperationException).GetProperty("Response")!.SetValue(ex, wrapper);
        return ex;
    }
}
