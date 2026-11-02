using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Songly.Common.Validation;

/// <summary>Upper bound in <b>UTF-8 bytes</b>, not characters. <c>null</c> is valid.</summary>
/// <remarks>
/// bcrypt only looks at the first 72 bytes of a password. A Polish letter takes 2 bytes and an
/// emoji 4, so a password within 72 <i>characters</i> can be far over 72 <i>bytes</i> — and two
/// passwords sharing their first 72 bytes would silently be the same password. Rejecting it is the
/// honest answer (original backend, ADR 0049).
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxUtf8BytesAttribute : ValidationAttribute
{
    public MaxUtf8BytesAttribute(int max)
    {
        Max = max;
        ErrorMessage = $"może mieć najwyżej {max} bajtów (litery takie jak ą czy ę zajmują po 2)";
    }

    public int Max { get; }

    public override bool IsValid(object? value) => value is not string text || Encoding.UTF8.GetByteCount(text) <= Max;
}
