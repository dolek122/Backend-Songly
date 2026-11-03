using Songly.Common.Validation;

namespace Songly.Profile.Http;

/// <summary>
/// Without <c>avatarUrl</c>, on purpose (original ADR 0010). It used to be a free-text field, so an
/// address controlled by a stranger was loaded by the client of everyone who opened the profile — a
/// ready-made tracking pixel, and a picture that could be swapped for anything at any time without
/// uploading anything to us. An avatar is set only by uploading a file. A client that still sends
/// the field is not rejected: unknown JSON properties are ignored.
/// </summary>
internal sealed record UpdateProfileRequest
{
    [NotBlank]
    [Size(0, 80)]
    public string? DisplayName { get; init; }

    [Size(0, 500)]
    public string? Bio { get; init; }
}
