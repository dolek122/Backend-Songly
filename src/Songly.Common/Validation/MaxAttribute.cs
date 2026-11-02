using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>A number at most <c>max</c> — Bean Validation's <c>@Max</c>. <c>null</c> is valid.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxAttribute : ValidationAttribute
{
    public MaxAttribute(long max)
    {
        Max = max;
        ErrorMessage = $"musi być mniejsze lub równe {max}";
    }

    public long Max { get; }

    public override bool IsValid(object? value) => value is null || Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) <= Max;
}
