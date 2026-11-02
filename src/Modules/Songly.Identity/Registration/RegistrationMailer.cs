using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Mail;

namespace Songly.Identity.Registration;

/// <summary>
/// The two messages one registration request can cause — and which of them goes out is the
/// <b>only</b> difference between a free address and a taken one. The HTTP response is identical
/// (202) in both cases; otherwise the endpoint would be a list of existing accounts.
/// </summary>
/// <remarks>
/// A delivery failure is swallowed and logged. Letting it out would turn the response into a 500 —
/// and a 500 that can happen in only one of the two branches would rebuild, through a mail
/// outage, the oracle the rest of this flow removes. The address is never logged; there is not
/// even an account to name for a free address.
/// </remarks>
internal sealed partial class RegistrationMailer(
    IMailSender mail,
    IOptions<RegistrationOptions> options,
    ILogger<RegistrationMailer> logger)
{
    private const string VerificationSubject = "Songly — potwierdź swój adres e-mail";
    private const string ExistingAccountSubject = "Songly — ktoś próbował założyć konto na Twój adres";

    public Task SendVerificationLinkAsync(string email, string token, CancellationToken cancellationToken)
    {
        var link = options.Value.VerificationUrl.Replace("{token}", token, StringComparison.Ordinal);
        var body = $"""
            Cześć!

            Ktoś (mamy nadzieję, że Ty) zakłada konto w Songly na ten adres.
            Potwierdź go, żeby dokończyć rejestrację:

            {link}

            Jeśli link nie otwiera się po kliknięciu, skopiuj go i wklej w aplikacji
            Songly, na ekranie potwierdzania adresu.

            Link wygasa po {(int)options.Value.Ttl.TotalHours} h. Do czasu potwierdzenia konto nie istnieje — jeśli
            to nie Ty, nie musisz robić nic, a my nie zapiszemy o Tobie niczego
            poza tym jednym zgłoszeniem, które samo wygaśnie.

            Zespół Songly
            """;
        return SendAsync(new OutgoingEmail(email, VerificationSubject, body), accountId: null, cancellationToken);
    }

    /// <summary>
    /// To the owner of an existing account. It points to the <b>password reset</b>, not to
    /// registration: if the owner forgot they already have an account, a reset is what they need;
    /// if it was a stranger, a reset is the one sensible reaction, and the owner controls it.
    /// </summary>
    public Task SendExistingAccountWarningAsync(Guid accountId, string email, CancellationToken cancellationToken)
    {
        const string body = """
            Cześć!

            Ktoś właśnie próbował założyć konto w Songly na ten adres — ale konto
            już tu istnieje, więc nie utworzyliśmy niczego nowego.

            Jeśli to byłeś Ty i nie pamiętasz hasła, użyj opcji „nie pamiętam
            hasła" w aplikacji. Jeśli to nie Ty — nic nie musisz robić. Nikt
            postronny nie dowiedział się od nas, że masz tu konto, i nikt nie
            uzyskał do niego dostępu.

            Zespół Songly
            """;
        return SendAsync(new OutgoingEmail(email, ExistingAccountSubject, body), accountId, cancellationToken);
    }

    private async Task SendAsync(OutgoingEmail email, Guid? accountId, CancellationToken cancellationToken)
    {
        try
        {
            await mail.SendAsync(email, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSendFailed(exception, accountId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send a registration e-mail, account: {AccountId}")]
    private partial void LogSendFailed(Exception exception, Guid? accountId);
}
