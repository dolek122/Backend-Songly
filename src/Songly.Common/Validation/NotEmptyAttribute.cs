using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>A collection that is not null and has at least one element — Bean Validation's <c>@NotEmpty</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotEmptyAttribute : ValidationAttribute
{
    public NotEmptyAttribute() => ErrorMessage = "nie może być puste";

    public override bool IsValid(object? value) => value is IEnumerable items && items.GetEnumerator().MoveNext();
}
