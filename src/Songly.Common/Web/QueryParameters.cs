using Microsoft.AspNetCore.Http;

namespace Songly.Common.Web;

/// <summary>Query parameters Minimal APIs do not bind the way the original's Spring MVC did.</summary>
public static class QueryParameters
{
    /// <summary>
    /// A required set of ids, accepted both as <c>?ids=a&amp;ids=b</c> and as <c>?ids=a,b</c> — Spring
    /// binds a collection from either, and the clients use both (the mobile app sends one
    /// comma-separated value). Duplicates collapse, as in Java's <c>Set</c>.
    /// </summary>
    /// <exception cref="MissingParameterException">The parameter is absent.</exception>
    /// <exception cref="InvalidParameterException">A value is not a UUID.</exception>
    public static IReadOnlySet<Guid> RequiredGuidSet(this HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            throw new MissingParameterException(name);
        }

        var ids = new HashSet<Guid>();
        foreach (var value in values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            ids.Add(Guid.TryParse(value, out var id) ? id : throw new InvalidParameterException(name));
        }

        return ids;
    }
}

/// <summary>The same error the framework's own binding gives for a missing parameter.</summary>
public sealed class MissingParameterException(string name)
    : ApiException(StatusCodes.Status400BadRequest, "MISSING_PARAMETER", $"Brak wymaganego parametru: {name}");

/// <summary>The same error the framework's own binding gives for a value of the wrong type.</summary>
public sealed class InvalidParameterException(string name)
    : ApiException(StatusCodes.Status400BadRequest, "INVALID_PARAMETER", $"Nieprawidłowa wartość parametru: {name}");
