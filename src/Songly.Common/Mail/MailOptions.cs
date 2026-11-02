namespace Songly.Common.Mail;

internal sealed class MailOptions
{
    public const string Section = "Songly:Mail";

    /// <summary>
    /// <c>log</c> (default) or <c>smtp</c>. The default is deliberate: an instance that sends a
    /// real e-mail by mistake is worse than one that only logs it by mistake.
    /// </summary>
    public string Provider { get; set; } = "log";

    public string From { get; set; } = "no-reply@songly.io";

    public SmtpOptions Smtp { get; set; } = new();
}
