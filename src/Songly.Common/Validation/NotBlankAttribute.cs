using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>Not null, not empty, not only whitespace — Bean Validation's <c>@NotBlank</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBlankAttribute : RequiredAttribute
{
    public NotBlankAttribute() => ErrorMessage = "nie może być puste";
}
