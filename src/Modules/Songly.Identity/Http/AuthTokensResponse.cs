namespace Songly.Identity.Http;

internal sealed record AuthTokensResponse(string AccessToken, string RefreshToken, string TokenType, long ExpiresInSeconds);
