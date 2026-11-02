using Songly.Common.Validation;

namespace Songly.Identity.Http;

internal sealed record RefreshRequest
{
    [NotBlank]
    public string? RefreshToken { get; init; }
}
