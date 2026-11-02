using Songly.Common.Validation;

namespace Songly.Identity.Http;

internal sealed record LoginRequest
{
    [NotBlank]
    [Email]
    public string? Email { get; init => field = EmailAddresses.Normalize(value); }

    [NotBlank]
    public string? Password { get; init; }
}
