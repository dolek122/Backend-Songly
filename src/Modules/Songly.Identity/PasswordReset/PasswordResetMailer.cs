using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Mail;

namespace Songly.Identity.PasswordReset;

/// <summary>Composes and sends the message with the reset link.</summary>
/// <remarks>
/// A delivery failure is <b>not</b> passed on. An exception here would turn the response into a
/// 500 — but only for addresses that have an account, because for the others there is nothing to
/// send. A mail-server outage would become exactly the oracle this flow avoids. The token is
/// already saved, so the user can simply ask again.
/// </remarks>
internal sealed partial class PasswordResetMailer(
    IMailSender mail,
    IOptions<PasswordResetOptions> options,
    ILogger<PasswordResetMailer> logger)
{
    private const string Subject = "Songly — reset hasła";

    public async Task SendResetLinkAsync(Guid accountId, string email, string token, CancellationToken cancellationToken)
    {
        try
        {
            await mail.SendAsync(new OutgoingEmail(email, Subject, Body(token)), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSendFailed(exception, accountId);
        }
    }

    /// <summary>
    /// The message says plainly what to do if the recipient did not ask for a reset — the only
    /// signal that someone may be trying to take over their account.
    /// </summary>
    private string Body(string token)
    {
        var link = options.Value.ResetUrl.Replace("{token}", token, StringComparison.Ordinal);
        return $"""
            Cześć!

            Otrzymaliśmy prośbę o zresetowanie hasła do Twojego konta w Songly.
            Ustaw nowe hasło, korzystając z poniższego linku:

            {link}

            Jeśli link nie otwiera się po kliknięciu, skopiuj go i wklej w aplikacji
            Songly, na ekranie resetu hasła.

            Link jest jednorazowy i wygasa za {PolishDuration.Format(options.Value.Ttl)}. Zresetowanie hasła wyloguje Cię
            ze wszystkich urządzeń.

            Jeśli to nie Ty prosiłeś o reset — zignoruj tę wiadomość. Twoje hasło
            pozostaje bez zmian, a link straci ważność sam z siebie.

            Zespół Songly
            """;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send the password reset e-mail for account {AccountId}")]
    private partial void LogSendFailed(Exception exception, Guid accountId);
}
