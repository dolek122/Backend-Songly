namespace Songly.Identity.Sessions;

/// <summary>
/// Somebody presented a refresh token that rotation had already consumed. A token exists in one
/// copy and may be used once, so a second use means another copy is in someone else's hands.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidRefreshTokenException"/> <b>on purpose</b>: the client gets
/// exactly the response of any other bad token. A distinct answer would tell an attacker that
/// their copy was detected and that it is worth racing the legitimate user. The separate type
/// exists for the server only.
/// </remarks>
internal sealed class RefreshTokenReuseDetectedException : InvalidRefreshTokenException;
