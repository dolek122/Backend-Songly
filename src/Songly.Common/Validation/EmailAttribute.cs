using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Songly.Common.Validation;

/// <summary>
/// A plausible e-mail address — Bean Validation's <c>@Email</c>. <c>null</c> is valid; combine with
/// <see cref="NotBlankAttribute"/>.
/// </summary>
/// <remarks>Deliberately lenient: the real check is the link sent to the address.</remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed partial class EmailAttribute : ValidationAttribute
{
    public EmailAttribute() => ErrorMessage = "musi być poprawnym adresem e-mail";

    public override bool IsValid(object? value) => value is not string text || Address().IsMatch(text);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex Address();
}
