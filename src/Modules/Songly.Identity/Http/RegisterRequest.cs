using Songly.Common.Validation;

namespace Songly.Identity.Http;

internal sealed record RegisterRequest
{
    [NotBlank]
    [Email]
    public string? Email { get; init => field = EmailAddresses.Normalize(value); }

    /// <summary>The upper bound is bcrypt's 72 bytes, counted in bytes (<see cref="MaxUtf8BytesAttribute"/>).</summary>
    [NotBlank]
    [Size(8, 72)]
    [MaxUtf8Bytes(72)]
    public string? Password { get; init; }
}
