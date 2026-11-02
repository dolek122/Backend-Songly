namespace Songly.Common.Mail;

/// <summary>
/// The e-mail channel. Infrastructure only: what to send and when is the business of the module
/// that sends it.
/// </summary>
public interface IMailSender
{
    /// <summary>
    /// Hands the message over for delivery. With SMTP this only queues it and returns at once —
    /// the response never waits for the mail server (original ADR 0041).
    /// </summary>
    /// <exception cref="MailDeliveryException">The message could not be accepted (for example, the queue is full).</exception>
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}
