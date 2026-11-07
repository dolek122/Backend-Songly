namespace Songly.Common.Push;

internal sealed class PushOptions
{
    public const string Section = "Songly:Push";

    /// <summary>
    /// <c>log</c> (default) or <c>fcm</c> — the mail channel's arrangement: an instance that sends a real
    /// push by mistake is worse than one that only logs it.
    /// </summary>
    public string Provider { get; set; } = PushProviders.Log;

    /// <summary>The JSON of the Firebase service account key itself, not a path — easier to keep in an environment variable.</summary>
    public string? FcmCredentialsJson { get; set; }

    public Uri FcmBaseUrl { get; set; } = new("https://fcm.googleapis.com");
}

internal static class PushProviders
{
    public const string Log = "log";

    public const string Fcm = "fcm";
}
