using Songly.Common.Mail;

namespace Songly.IntegrationTests;

/// <summary>A mail channel whose server is down.</summary>
public sealed class FailingMailSender : IMailSender
{
    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken) =>
        throw new MailDeliveryException("The mail server is down");
}
