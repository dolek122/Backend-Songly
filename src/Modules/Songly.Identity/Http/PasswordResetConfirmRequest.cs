using Songly.Common.Validation;

namespace Songly.Identity.Http;

/// <summary>
/// The rules for <see cref="NewPassword"/> are those of registration and password change — a reset
/// must not be a way around the password policy. Deliberately no current password: the whole point
/// of this path is that the user does not know it. Proof of identity here is access to the mailbox.
/// </summary>
internal sealed record PasswordResetConfirmRequest
{
    [NotBlank]
    public string? Token { get; init; }

    [NotBlank]
    [Size(8, 72)]
    [MaxUtf8Bytes(72)]
    public string? NewPassword { get; init; }
}
