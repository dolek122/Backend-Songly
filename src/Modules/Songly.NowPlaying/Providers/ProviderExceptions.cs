using System.Net;
using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.NowPlaying.Providers;

/// <summary>
/// The provider answered with an error. An <see cref="ApiException"/> with 502 on purpose, not a
/// bare exception: otherwise every hiccup on the provider's side became our 500 with a stack trace,
/// someone else's outage looking like ours in the monitoring.
/// </summary>
/// <remarks>
/// One code outward — the client reacts the same way ("try later"). The distinctions it carries are
/// for us: <c>invalid_grant</c> becomes a meaningful answer in the provider, and
/// <see cref="RetryAfter"/> tells the poll to stop asking an API that just said "enough".
/// </remarks>
internal sealed class SpotifyApiException(HttpStatusCode upstreamStatus, string? error, TimeSpan? retryAfter)
    : ApiException(StatusCodes.Status502BadGateway, "MUSIC_PROVIDER_ERROR", "Serwis muzyczny odpowiedział błędem")
{
    public HttpStatusCode UpstreamStatus { get; } = upstreamStatus;

    /// <summary>Spotify's code for consent taken back, or an authorization code already used.</summary>
    public bool IsInvalidGrant { get; } = error == "invalid_grant";

    public bool IsRateLimited => UpstreamStatus == HttpStatusCode.TooManyRequests;

    /// <summary><c>null</c> when Spotify gave no <c>Retry-After</c>.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The authorization code from Spotify's redirect is used up, expired or forged — a client error
/// (400), not a provider outage, or refreshing the callback in the browser would report a 502.
/// </summary>
internal sealed class InvalidAuthorizationCodeException()
    : ApiException(StatusCodes.Status400BadRequest, "INVALID_AUTHORIZATION_CODE", "Kod autoryzacyjny jest nieprawidłowy lub wygasł");

internal sealed class InvalidOAuthStateException()
    : ApiException(StatusCodes.Status400BadRequest, "INVALID_OAUTH_STATE", "Parametr state jest nieprawidłowy lub wygasł");

/// <summary>
/// The user took their consent back in Spotify — the link was deleted and OAuth has to be done again.
/// A code of its own: "never connected" and "connected, and it stopped working" are two different
/// messages in the app.
/// </summary>
internal sealed class ProviderReauthorizationRequiredException()
    : ApiException(StatusCodes.Status409Conflict, "PROVIDER_REAUTHORIZATION_REQUIRED", "Utracono dostęp do serwisu muzycznego — podłącz go ponownie");
