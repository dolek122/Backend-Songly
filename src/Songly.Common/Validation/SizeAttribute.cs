using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>
/// Length in characters within [min, max] — Bean Validation's <c>@Size</c>. <c>null</c> is valid.
/// </summary>
/// <remarks>
/// Counts UTF-16 code units, exactly like Java's <c>String.length()</c>, so the limits mean the
/// same thing as in the original backend.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SizeAttribute : ValidationAttribute
{
    public SizeAttribute(int min, int max)
    {
        Min = min;
        Max = max;
        ErrorMessage = $"musi mieć od {min} do {max} znaków";
    }

    public int Min { get; }

    public int Max { get; }

    public override bool IsValid(object? value) => value is not string text || (text.Length >= Min && text.Length <= Max);
}
