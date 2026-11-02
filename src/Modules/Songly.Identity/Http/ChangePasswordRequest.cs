using Songly.Common.Validation;

namespace Songly.Identity.Http;

/// <summary>
/// The rules for <see cref="NewPassword"/> are the registration rules — looser ones would make a
/// password change a way around the password policy. The upper bound of 72 is bcrypt's limit, not
/// a choice, and it counts bytes (<see cref="MaxUtf8BytesAttribute"/>).
/// </summary>
internal sealed record ChangePasswordRequest
{
    [NotBlank]
    public string? CurrentPassword { get; init; }

    [NotBlank]
    [Size(8, 72)]
    [MaxUtf8Bytes(72)]
    public string? NewPassword { get; init; }
}
