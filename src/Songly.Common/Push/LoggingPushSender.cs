using Microsoft.Extensions.Logging;

namespace Songly.Common.Push;

/// <summary>
/// The default for development and tests: logs the push instead of sending it, like the mail channel's
/// logging sender. Unlike a mail, <b>without its text</b> — the text names another person, and the type
/// is enough to see a push went out. Only the start of the token: the whole token is the address of
/// someone's phone.
/// </summary>
internal sealed partial class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        LogPush(message.Token[..Math.Min(8, message.Token.Length)], message.Data.GetValueOrDefault("type"));
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[PUSH:log] device={TokenStart}… type={Type}")]
    private partial void LogPush(string tokenStart, string? type);
}
