using Microsoft.Extensions.Options;

namespace Songly.Common.Mail;

/// <summary>
/// Checked at startup. <c>smtp</c> without a host fails the start on purpose: an instance that only
/// looks as if it sent mail is an outage nobody notices until the first "I never got the link".
/// </summary>
internal sealed class MailOptionsValidator : IValidateOptions<MailOptions>
{
    public ValidateOptionsResult Validate(string? name, MailOptions options) => options.Provider switch
    {
        MailProviders.Log => ValidateOptionsResult.Success,
        MailProviders.Smtp when string.IsNullOrWhiteSpace(options.Smtp.Host) => ValidateOptionsResult.Fail(
            $"{MailOptions.Section}:Provider=smtp requires {MailOptions.Section}:Smtp:Host — without it the application " +
            "would start with no working e-mail delivery (verification and password reset would silently stop working)"),
        MailProviders.Smtp => ValidateOptionsResult.Success,
        _ => ValidateOptionsResult.Fail($"{MailOptions.Section}:Provider must be '{MailProviders.Log}' or '{MailProviders.Smtp}'"),
    };
}
