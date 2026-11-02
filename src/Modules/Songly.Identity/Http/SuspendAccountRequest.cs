using Songly.Common.Validation;

namespace Songly.Identity.Http;

/// <summary>
/// The reason is <b>mandatory</b>, unlike a community ban's: a suspension cuts the account off the
/// whole service, so a decision without a reason could not be reconstructed — by another
/// administrator or by the suspended user, who will see it.
/// </summary>
internal sealed record SuspendAccountRequest
{
    [NotBlank]
    [Size(0, 500)]
    public string? Reason { get; init; }

    /// <summary>
    /// <c>null</c> = indefinitely. At most 100 years: a longer term overflows Postgres's timestamp
    /// range and used to end in a 500 instead of a 400 (original ADR 0049).
    /// </summary>
    [Min(1)]
    [Max(36_500)]
    public int? DurationDays { get; init; }
}
