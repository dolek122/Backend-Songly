using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Songly.Common.Mail;

/// <summary>
/// The default for development and tests: writes the message to the log instead of sending it.
/// </summary>
/// <remarks>
/// Not a stub for the sake of having one — without it, walking through registration locally
/// would need a real SMTP server, and the first thing anyone would do is dig the token out of the
/// database. It logs the <b>whole message, token included</b>, which is exactly why it is for
/// development only: in production anyone with access to the logs could take over accounts.
/// </remarks>
internal sealed partial class LoggingMailSender(IOptions<MailOptions> options, ILogger<LoggingMailSender> logger) : IMailSender
{
    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        LogMail(options.Value.From, email.To, email.Subject, email.Body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[MAIL:log] from={From} to={To} subject={Subject}\n{Body}")]
    private partial void LogMail(string from, string to, string subject, string body);
}
