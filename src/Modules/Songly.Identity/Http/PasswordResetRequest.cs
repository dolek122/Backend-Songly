using Songly.Common.Validation;

namespace Songly.Identity.Http;

internal sealed record PasswordResetRequest
{
    [NotBlank]
    [Email]
    public string? Email { get; init => field = EmailAddresses.Normalize(value); }
}
