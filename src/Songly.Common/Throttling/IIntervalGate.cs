namespace Songly.Common.Throttling;

/// <summary>
/// "At most once per interval" for a key — the shared mechanism behind every "do not send this
/// again so soon" rule (warning mails, resend intervals, moderation mail spacing).
/// </summary>
public interface IIntervalGate
{
    /// <summary>
    /// <c>true</c> if nobody passed with this key within <paramref name="interval"/> — and then the
    /// interval starts now. Also <c>true</c> when the store is unavailable (fail-open, see
    /// <see cref="RedisIntervalGate"/>).
    /// </summary>
    Task<bool> TryPassAsync(string key, TimeSpan interval);
}
