using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Mail;

namespace Songly.Identity.AccountDeletion;

/// <summary>
/// The message with the link that confirms the deletion (original ADR 0043). Sent after the token is
/// saved, and a delivery failure is swallowed — for the same reasons as the password-reset mail: the
/// link must lead to a token that exists, and a mail failure must not become a response that marks
/// existing accounts.
/// </summary>
internal sealed partial class AccountDeletionMailer(
    IMailSender mail,
    IOptions<AccountDeletionOptions> options,
    ILogger<AccountDeletionMailer> logger)
{
    private const string Subject = "Songly — potwierdź usunięcie konta";

    public async Task SendConfirmationLinkAsync(Guid accountId, string email, string token, CancellationToken cancellationToken)
    {
        var link = options.Value.ConfirmUrl.Replace("{token}", token, StringComparison.Ordinal);
        var body = $"""
            Cześć!

            Otrzymaliśmy prośbę o usunięcie Twojego konta w Songly. Żeby ją
            potwierdzić, otwórz poniższy link — zobaczysz stronę z przyciskiem,
            który usuwa konto na stałe:

            {link}

            Samo otwarcie linku niczego nie usuwa. Link jest ważny przez {(int)options.Value.Ttl.TotalMinutes} min.

            Usunięcia nie da się cofnąć: znikną profil, zdjęcia, posty, komentarze,
            znajomości i społeczności, których jesteś właścicielem.

            Jeśli to nie Ty prosiłeś o usunięcie konta — zignoruj tę wiadomość.
            Bez kliknięcia przycisku na stronie nic się nie stanie.

            Zespół Songly
            """;
        try
        {
            await mail.SendAsync(new OutgoingEmail(email, Subject, body), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSendFailed(exception, accountId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send the account deletion link for account {AccountId}")]
    private partial void LogSendFailed(Exception exception, Guid accountId);
}
