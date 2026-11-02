using Songly.Common.Mail;

namespace Songly.Common.Tests;

/// <summary>
/// The startup check of the mail channel. Tested here, directly, because asserting the message of
/// a failed start through <c>WebApplicationFactory</c> is racy (see <c>MailChannelTests</c>).
/// </summary>
public sealed class MailOptionsValidatorTests
{
    private static readonly MailOptionsValidator Validator = new();

    [Fact]
    public void The_log_provider_needs_nothing_else() =>
        Assert.True(Validator.Validate(null, new MailOptions { Provider = "log" }).Succeeded);

    /// <summary>
    /// An instance that only looks as if it sent mail is an outage nobody notices until the first
    /// "I never got the link".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Smtp_without_a_host_is_rejected(string? host)
    {
        var result = Validator.Validate(null, new MailOptions { Provider = "smtp", Smtp = new SmtpOptions { Host = host } });

        Assert.True(result.Failed);
        Assert.Contains("Songly:Mail:Smtp:Host", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_with_a_host_is_accepted() =>
        Assert.True(Validator.Validate(null, new MailOptions { Provider = "smtp", Smtp = new SmtpOptions { Host = "smtp.example.com" } }).Succeeded);

    [Fact]
    public void An_unknown_provider_is_rejected()
    {
        var result = Validator.Validate(null, new MailOptions { Provider = "carrier-pigeon" });

        Assert.True(result.Failed);
        Assert.Contains("'log' or 'smtp'", result.FailureMessage, StringComparison.Ordinal);
    }
}
