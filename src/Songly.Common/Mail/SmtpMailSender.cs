using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Songly.Common.Mail;

/// <summary>Delivers one message over SMTP (MailKit). Used only behind <see cref="QueuedMailSender"/>.</summary>
internal sealed class SmtpMailSender(IOptions<MailOptions> options)
{
    public async Task DeliverAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        message.Body = new TextPart("plain") { Text = email.Body };

        using var client = new SmtpClient { Timeout = (int)settings.Smtp.Timeout.TotalMilliseconds };
        try
        {
            // Auto: TLS from the start on 465, STARTTLS whenever the server offers it otherwise.
            // The host is guaranteed by MailOptionsValidator at startup.
            await client.ConnectAsync(settings.Smtp.Host!, settings.Smtp.Port, SecureSocketOptions.Auto, cancellationToken);
            if (!string.IsNullOrEmpty(settings.Smtp.Username))
            {
                await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MailDeliveryException("Failed to send an e-mail over SMTP", exception);
        }
    }
}
