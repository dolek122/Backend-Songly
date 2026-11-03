namespace Songly.Common.Web;

/// <summary>
/// Base class for every expected, domain-level failure. Carries the HTTP status and the error
/// code that end up in <see cref="ApiError"/>.
/// </summary>
/// <remarks>
/// Throwing an <see cref="ApiException"/> means "the request was wrong or conflicts with the
/// current state", never "the server is broken" — so it is never logged with a stack trace.
/// Anything else that escapes an endpoint is a 500 and is logged.
/// </remarks>
public abstract class ApiException : Exception
{
    protected ApiException(int statusCode, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <summary>HTTP status code of the response.</summary>
    public int StatusCode { get; }

    /// <summary>Stable, machine-readable error code (for example <c>INVALID_CREDENTIALS</c>).</summary>
    public string Code { get; }
}
