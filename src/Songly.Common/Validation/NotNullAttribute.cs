using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>Present — Bean Validation's <c>@NotNull</c>, for values that are not text (an id, a number).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotNullAttribute : ValidationAttribute
{
    public NotNullAttribute() => ErrorMessage = "nie może mieć wartości null";

    public override bool IsValid(object? value) => value is not null;
}
