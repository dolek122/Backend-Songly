namespace Songly.Common.Web;

/// <summary>
/// The id of the request being handled, for work that outlives it (a mail sent from a queue).
/// </summary>
/// <remarks>
/// The logging scope ends with the request, so a background worker cannot see it. Work handed
/// off to the background captures <see cref="Current"/> and reopens a scope with it — a failure
/// there is then logged under the same <c>requestId</c> as the request that caused it.
/// </remarks>
public static class RequestCorrelation
{
    private static readonly AsyncLocal<string?> CurrentRequestId = new();

    /// <summary>The current request's id, or <c>null</c> outside a request.</summary>
    public static string? Current
    {
        get => CurrentRequestId.Value;
        internal set => CurrentRequestId.Value = value;
    }
}
