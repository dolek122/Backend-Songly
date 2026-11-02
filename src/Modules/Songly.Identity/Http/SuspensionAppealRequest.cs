using Songly.Common.Validation;

namespace Songly.Identity.Http;

/// <summary>
/// The text is mandatory — an appeal without a justification gives the administrators nothing to
/// consider. Longer than the suspension reason (500), because it answers that reason.
/// </summary>
internal sealed record SuspensionAppealRequest
{
    [NotBlank]
    [Size(0, 1000)]
    public string? Text { get; init; }
}
