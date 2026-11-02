using Songly.Common.Validation;

namespace Songly.Identity.Http;

internal sealed record VerifyEmailRequest
{
    [NotBlank]
    public string? Token { get; init; }
}
