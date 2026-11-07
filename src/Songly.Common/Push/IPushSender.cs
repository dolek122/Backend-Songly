namespace Songly.Common.Push;

/// <summary>
/// The push channel (original ADR 0040). Infrastructure without domain knowledge: it gets a ready text
/// and a device token, and knows nothing of notifications or users.
/// </summary>
public interface IPushSender
{
    /// <exception cref="InvalidPushTokenException">The provider says this device is gone — the caller must forget the token, or every later push lands nowhere.</exception>
    /// <exception cref="PushDeliveryException">Any other failure to send.</exception>
    Task SendAsync(PushMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// One push. The title and body travel in <paramref name="Data"/>, not in FCM's <c>notification</c> block:
/// the app builds the notification itself, so it decides the channel and what a tap opens, in the
/// background too.
/// </summary>
public sealed record PushMessage(string Token, string Title, string Body, IReadOnlyDictionary<string, string> Data);

/// <summary>The provider no longer knows this token (the app was uninstalled, the token replaced).</summary>
public sealed class InvalidPushTokenException(string message) : Exception(message);

public sealed class PushDeliveryException : Exception
{
    public PushDeliveryException(string message)
        : base(message)
    {
    }

    public PushDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
