using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Songly.Common.Web;

namespace Songly.Common.Mail;

/// <summary>
/// SMTP delivery outside the request (original ADR 0041): <see cref="SendAsync"/> only queues the
/// message, and background workers deliver it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is about response time, not convenience.</b> Some flows send a message only when an
/// account exists (password reset) or send different messages depending on it (registration). An
/// SMTP conversation takes hundreds of milliseconds, so delivering inside the request made the
/// response time itself reveal whether an address has an account. A hung mail server also held
/// request threads, of which there are finitely many.
/// </para>
/// <para>
/// The queue is bounded. When it is full, <see cref="SendAsync"/> throws
/// <see cref="MailDeliveryException"/>, which callers already handle (the message is lost with a
/// log entry). Blocking the request until there is room would bring back both problems at once.
/// </para>
/// <para>
/// A delivery failure is logged under the <c>requestId</c> of the request that queued the
/// message — never with the recipient's address. On shutdown the workers drain what was already
/// accepted, within the host's shutdown timeout.
/// </para>
/// </remarks>
internal sealed partial class QueuedMailSender(SmtpMailSender smtp, ILogger<QueuedMailSender> logger) : BackgroundService, IMailSender
{
    /// <summary>
    /// Mail goes out on registration, password reset and moderation decisions — not on ordinary
    /// activity — so two workers are plenty.
    /// </summary>
    private const int Workers = 2;

    private const int Capacity = 500;

    private readonly Channel<QueuedMail> _queue = Channel.CreateBounded<QueuedMail>(new BoundedChannelOptions(Capacity)
    {
        SingleWriter = false,
        SingleReader = false,
    });

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken) =>
        _queue.Writer.TryWrite(new QueuedMail(email, RequestCorrelation.Current))
            ? Task.CompletedTask
            : throw new MailDeliveryException("The e-mail queue is full");

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Accept nothing new; the workers finish what is already queued.
        _queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => DeliverQueuedAsync()));

    private async Task DeliverQueuedAsync()
    {
        // Not the stopping token: reading ends when StopAsync completes the queue, so whatever was
        // accepted before shutdown still goes out.
        await foreach (var mail in _queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            using var scope = mail.RequestId is null
                ? null
                : logger.BeginScope(new Dictionary<string, object> { [RequestIdMiddleware.LogProperty] = mail.RequestId });
            try
            {
                await smtp.DeliverAsync(mail.Email, CancellationToken.None);
            }
            catch (MailDeliveryException exception)
            {
                LogDeliveryFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deliver an e-mail")]
    private partial void LogDeliveryFailed(Exception exception);

    private sealed record QueuedMail(OutgoingEmail Email, string? RequestId);
}
