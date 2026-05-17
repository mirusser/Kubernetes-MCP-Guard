using System.Net;
using System.Net.Http;
using InfraGate.McpServer;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerHelpersTests
{
    [Fact]
    public void IsConflict_KubernetesExceptionWithConflictCode_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 409 };
        var ex = new KubernetesException(status);

        var result = K8sManager.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_KubernetesExceptionWithConflictReason_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 500, Reason = "Conflict" };
        var ex = new KubernetesException(status);

        var result = K8sManager.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_HttpOperationExceptionWithConflictStatus_ReturnsTrue()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.Conflict);

        var result = K8sManager.IsConflict(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_KubernetesExceptionWithoutConflict_ReturnsFalse()
    {
        var status = new k8s.Models.V1Status { Code = 404, Reason = "NotFound" };
        var ex = new KubernetesException(status);

        var result = K8sManager.IsConflict(ex);

        Assert.False(result);
    }

    [Fact]
    public void IsConflict_NonMatchingException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("test");

        var result = K8sManager.IsConflict(ex);

        Assert.False(result);
    }

    [Fact]
    public void FormatServerSideApplyException_WithConflict_IncludesApplyConflictMessage()
    {
        var status = new k8s.Models.V1Status { Code = 409, Reason = "Conflict", Message = "field is immutable" };
        var ex = new KubernetesException(status);

        var result = K8sManager.FormatServerSideApplyException("Apply failed", ex);

        Assert.Contains("Apply refused by Kubernetes field ownership conflict", result);
        Assert.Contains("field is immutable", result);
    }

    [Fact]
    public void FormatServerSideApplyException_WithoutConflict_OmitsConflictMessage()
    {
        var status = new k8s.Models.V1Status { Code = 404, Reason = "NotFound", Message = "not found" };
        var ex = new KubernetesException(status);

        var result = K8sManager.FormatServerSideApplyException("Apply failed", ex);

        Assert.DoesNotContain("field ownership conflict", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public void TryFormatKubernetesException_WithStatus_ReturnsTrue()
    {
        var status = new k8s.Models.V1Status { Code = 500, Reason = "InternalError", Message = "server error" };
        var ex = new KubernetesException(status);

        var result = K8sManager.TryFormatKubernetesException("prefix", ex, out var message);

        Assert.True(result);
        Assert.Contains("500 InternalError: server error", message);
    }

    [Fact]
    public void TryFormatKubernetesException_WithNullStatus_ReturnsFalse()
    {
        var ex = new KubernetesException();

        var result = K8sManager.TryFormatKubernetesException("prefix", ex, out var message);

        Assert.False(result);
        Assert.Empty(message);
    }

    [Fact]
    public void TryFormatHttpOperationException_WithResponse_ReturnsTrue()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.InternalServerError, "Server Error", "request failed");

        var result = K8sManager.TryFormatHttpOperationException("prefix", ex, out var message);

        Assert.True(result);
        Assert.Contains("500 Server Error", message);
    }

    [Fact]
    public void TryFormatHttpOperationException_WithNullResponse_ReturnsFalse()
    {
        var ex = new HttpOperationException("generic error");

        var result = K8sManager.TryFormatHttpOperationException("prefix", ex, out var message);

        Assert.False(result);
        Assert.Empty(message);
    }

    [Fact]
    public void FormatApiException_NonKubernetesNonHttpOperationException_ReturnsFallbackMessage()
    {
        var ex = new ArgumentException("invalid argument");

        var result = K8sManager.FormatApiException("prefix", ex);

        Assert.Equal("prefix: invalid argument", result);
    }

    [Fact]
    public void FormatApiException_HttpOperationException_ReturnsHttpFormatMessage()
    {
        var ex = CreateHttpOperationException(HttpStatusCode.BadRequest, "Bad Request", "invalid input");

        var result = K8sManager.FormatApiException("prefix", ex);

        Assert.Contains("400 Bad Request", result);
    }

    [Fact]
    public void FormatApiException_KubernetesException_ReturnsKubernetesFormatMessage()
    {
        var status = new k8s.Models.V1Status { Code = 422, Reason = "UnprocessableEntity", Message = "validation failed" };
        var ex = new KubernetesException(status);

        var result = K8sManager.FormatApiException("prefix", ex);

        Assert.Contains("422 UnprocessableEntity: validation failed", result);
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
