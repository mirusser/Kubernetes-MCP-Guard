using System.Net.Mail;
using InfraGate.McpGateway.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class SmtpApprovalEmailSenderTests
{
    private static SmtpApprovalEmailOptions ValidOptions =>
        new("smtp.example.com", 587, "noreply@example.com");

    [Fact]
    public async Task SendAsync_NullContent_ThrowsArgumentNullException()
    {
        var sender = new SmtpApprovalEmailSender(
            ValidOptions,
            new StubSmtpClientFactory(Task.CompletedTask),
            NullLogger<SmtpApprovalEmailSender>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sender.SendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_ValidContent_CallsSmtpClientSendMailAsync()
    {
        var content = new ApprovalEmailContent("user@test.com", "Approval Required", "Please approve.");
        bool sendCalled = false;
        var stub = new StubSmtpClientFactory(Task.CompletedTask, onSend: _ => sendCalled = true);

        var sender = new SmtpApprovalEmailSender(
            ValidOptions,
            stub,
            NullLogger<SmtpApprovalEmailSender>.Instance);

        await sender.SendAsync(content, CancellationToken.None);

        Assert.True(sendCalled);
    }

    [Fact]
    public async Task SendAsync_SendMailThrows_PropagatesException()
    {
        var content = new ApprovalEmailContent("user@test.com", "Subject", "Body");
        var stub = new StubSmtpClientFactory(Task.FromException(new InvalidOperationException("smtp down")));

        var sender = new SmtpApprovalEmailSender(
            ValidOptions,
            stub,
            NullLogger<SmtpApprovalEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_OptionsWithInvalidPort_ThrowsBeforeSend()
    {
        var options = new SmtpApprovalEmailOptions("smtp.example.com", 0, "noreply@example.com");
        bool sendCalled = false;
        var stub = new StubSmtpClientFactory(Task.CompletedTask, onSend: _ => sendCalled = true);

        var sender = new SmtpApprovalEmailSender(
            options,
            stub,
            NullLogger<SmtpApprovalEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(new ApprovalEmailContent("x@x.com", "s", "b"), CancellationToken.None));

        Assert.False(sendCalled);
    }

    private sealed class StubSmtpClientFactory(Task result, Action<MailMessage>? onSend = null)
        : ISmtpClientFactory
    {
        public ISmtpClient Create(SmtpApprovalEmailOptions options) =>
            new StubSmtpClient(result, onSend);
    }

    private sealed class StubSmtpClient(Task result, Action<MailMessage>? onSend) : ISmtpClient
    {
        public bool EnableSsl => false;

        public Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
        {
            onSend?.Invoke(message);
            return result;
        }

        public void Dispose() { }
    }
}

public sealed class SmtpApprovalEmailOptionsTests
{
    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", 25, "from@test.com");
        options.Validate();
    }

    [Fact]
    public void Validate_EmptyHost_ThrowsArgumentException()
    {
        var options = new SmtpApprovalEmailOptions("", 25, "from@test.com");
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WhitespaceHost_ThrowsArgumentException()
    {
        var options = new SmtpApprovalEmailOptions("   ", 25, "from@test.com");
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_EmptyFromAddress_ThrowsArgumentException()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", 25, "");
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_PortZero_ThrowsInvalidOperationException()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", 0, "from@test.com");
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_PortNegative_ThrowsInvalidOperationException()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", -1, "from@test.com");
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_PortTooHigh_ThrowsInvalidOperationException()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", 65536, "from@test.com");
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Port65535_DoesNotThrow()
    {
        var options = new SmtpApprovalEmailOptions("smtp.test.com", 65535, "from@test.com");
        options.Validate();
    }

    [Fact]
    public void DefaultPort_Is25()
    {
        Assert.Equal(25, SmtpApprovalEmailOptions.DefaultPort);
    }

    [Fact]
    public void DefaultEnableSsl_IsTrue()
    {
        Assert.True(SmtpApprovalEmailOptions.DefaultEnableSsl);
    }
}
