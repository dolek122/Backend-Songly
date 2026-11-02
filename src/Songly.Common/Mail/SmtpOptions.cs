namespace Songly.Common.Mail;

internal sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Per connect, send and disconnect. Without a timeout a hung mail server holds a sending
    /// worker forever, and the queue behind it fills up.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
