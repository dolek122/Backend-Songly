using Songly.Common.Validation;

namespace Songly.Identity.Http;

/// <summary>The password again: a valid access token alone is not enough for an irreversible action.</summary>
internal sealed record DeleteAccountRequest
{
    [NotBlank]
    public string? Password { get; init; }
}
