namespace Songly.Common.Web;

/// <summary>
/// The single error format of the whole API: <c>code</c>, <c>message</c>, <c>timestamp</c>,
/// <c>path</c>. Identical to the original Java backend, because the mobile app parses it.
/// </summary>
public sealed record ApiError(string Code, string Message, DateTimeOffset Timestamp, string Path);
