using System.ComponentModel.DataAnnotations;

namespace Songly.Common.Validation;

/// <summary>A number at least <c>min</c> — Bean Validation's <c>@Min</c>. <c>null</c> is valid.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinAttribute : ValidationAttribute
{
    public MinAttribute(long min)
    {
        Min = min;
        ErrorMessage = $"musi być większe lub równe {min}";
    }

    public long Min { get; }

    public override bool IsValid(object? value) => value is null || Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) >= Min;
}
