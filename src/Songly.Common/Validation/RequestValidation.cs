using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Songly.Common.Validation;

/// <summary>
/// Validates endpoint arguments against their validation attributes before the handler runs, and
/// answers <c>400 VALIDATION_ERROR</c> with <c>field: message</c> pairs — the format of the
/// original backend.
/// </summary>
/// <remarks>
/// The host applies this to the group every module maps its endpoints onto, so validation is on
/// by default: a new request type is validated as soon as it declares an attribute, and nobody
/// has to remember to opt in. Endpoints whose arguments declare no attributes pay nothing — the
/// decision is made once, when the endpoint is built.
/// </remarks>
public static class RequestValidation
{
    private static readonly ConcurrentDictionary<Type, bool> Validatable = new();

    public static TBuilder ValidateRequests<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilterFactory(CreateFilter);

    private static EndpointFilterDelegate CreateFilter(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var validatedArguments = context.MethodInfo.GetParameters()
            .Select((parameter, index) => (parameter.ParameterType, index))
            .Where(argument => HasValidationAttributes(argument.ParameterType))
            .Select(argument => argument.index)
            .ToArray();

        if (validatedArguments.Length == 0)
        {
            return next;
        }

        return invocation =>
        {
            var errors = validatedArguments
                .Select(index => invocation.Arguments[index])
                .OfType<object>()
                .SelectMany(Validate)
                .ToList();

            return errors.Count == 0
                ? next(invocation)
                : throw new RequestValidationException(string.Join("; ", errors));
        };
    }

    private static IEnumerable<string> Validate(object argument)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true);
        return results.Select(result => $"{JsonName(result.MemberNames)}: {result.ErrorMessage}");
    }

    // The client knows the field by its JSON name, not by the C# property name.
    private static string JsonName(IEnumerable<string> memberNames) =>
        string.Join(", ", memberNames.Select(JsonNamingPolicy.CamelCase.ConvertName));

    private static bool HasValidationAttributes(Type type) => Validatable.GetOrAdd(type, static candidate =>
        candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.IsDefined(typeof(ValidationAttribute), inherit: true)));
}
