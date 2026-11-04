using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.NowPlaying.Domain;
using Songly.NowPlaying.Persistence;

namespace Songly.NowPlaying.Providers;

/// <summary>Spotify: OAuth (scope <c>user-read-currently-playing</c>) and "Get Currently Playing Track", with the tokens refreshed as they expire.</summary>
/// <remarks>
/// <para>
/// Each change is saved the moment it is true, and that is what the original needed
/// <c>noRollbackFor</c> for. Its whole read ran in one transaction, so an exception thrown after a
/// change rolled the change back: the dead link deleted on <c>invalid_grant</c> came back (and the
/// poll knocked on it every 30 seconds for ever), and a token refreshed just before a failing API call
/// was lost (with a rotated refresh token, the stale one could later get <c>invalid_grant</c> and
/// delete a healthy link — original ADR 0049). Here there is no transaction around the read: the
/// refreshed tokens and the deleted link are each saved before the next step can fail.
/// </para>
/// </remarks>
internal sealed class SpotifyProvider(
    SpotifyClient spotify,
    NowPlayingDbContext db,
    TokenEncryptor tokens,
    IOptions<SpotifyOptions> options,
    TimeProvider time) : IMusicProvider
{
    public const string ProviderName = "spotify";

    public string Name => ProviderName;

    public string AuthorizeUrl(string state) =>
        QueryHelpers.AddQueryString(options.Value.AuthorizeUrl, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = options.Value.ClientId,
            ["scope"] = "user-read-currently-playing",
            ["redirect_uri"] = options.Value.RedirectUri,
            ["state"] = state,
        });

    public async Task ConnectAsync(Guid userId, string authorizationCode, CancellationToken cancellationToken)
    {
        var granted = await ExchangeCodeAsync(authorizationCode, cancellationToken);
        var now = time.GetUtcNow();
        var expiresAt = now.AddSeconds(granted.ExpiresInSeconds);
        var access = tokens.Encrypt(granted.AccessToken);
        var refresh = tokens.Encrypt(granted.RefreshToken ?? string.Empty);
        var link = await db.Links.SingleOrDefaultAsync(l => l.UserId == userId && l.Provider == ProviderName, cancellationToken);
        if (link is null)
        {
            db.Links.Add(new MusicProviderLink(userId, ProviderName, access, refresh, expiresAt, now));
        }
        else
        {
            link.UpdateTokens(access, refresh, expiresAt, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The caller checked the link exists; a link gone in the meantime reads as nothing playing.</summary>
    public async Task<NowPlayingTrack?> CurrentlyPlayingAsync(Guid userId, CancellationToken cancellationToken)
    {
        var link = await db.Links.SingleOrDefaultAsync(l => l.UserId == userId && l.Provider == ProviderName, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var response = await spotify.CurrentlyPlayingAsync(await FreshAccessTokenAsync(link, cancellationToken), cancellationToken);
        return response?.Item is { } item ? ToTrack(response.IsPlaying, item) : null;
    }

    /// <summary>Here <c>invalid_grant</c> means "a bad code from the redirect", not "consent taken back" — there is no link yet that could die.</summary>
    private async Task<SpotifyClient.TokenResponse> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken)
    {
        try
        {
            return await spotify.ExchangeCodeAsync(authorizationCode, cancellationToken);
        }
        catch (SpotifyApiException exception) when (exception.IsInvalidGrant)
        {
            throw new InvalidAuthorizationCodeException();
        }
    }

    private async Task<string> FreshAccessTokenAsync(MusicProviderLink link, CancellationToken cancellationToken)
    {
        if (!link.IsAccessTokenExpired(time.GetUtcNow()))
        {
            return tokens.Decrypt(link.AccessTokenEncrypted);
        }

        var refreshToken = tokens.Decrypt(link.RefreshTokenEncrypted);
        var refreshed = await RefreshAsync(link, refreshToken, cancellationToken);

        // Spotify usually returns no new refresh token on a refresh_token grant.
        var now = time.GetUtcNow();
        link.UpdateTokens(tokens.Encrypt(refreshed.AccessToken), tokens.Encrypt(refreshed.RefreshToken ?? refreshToken), now.AddSeconds(refreshed.ExpiresInSeconds), now);
        await db.SaveChangesAsync(cancellationToken);
        return refreshed.AccessToken;
    }

    /// <summary>
    /// <c>invalid_grant</c> on a refresh cannot be undone — the user took consent back in Spotify's
    /// settings, and this refresh token will never work again. Keeping the link gains nothing and costs
    /// a failed call on every run of the poll, for ever, so it is deleted at the first proof. The cached
    /// status is left to expire on its own.
    /// </summary>
    private async Task<SpotifyClient.TokenResponse> RefreshAsync(MusicProviderLink link, string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            return await spotify.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (SpotifyApiException exception) when (exception.IsInvalidGrant)
        {
            db.Links.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            throw new ProviderReauthorizationRequiredException();
        }
    }

    private static NowPlayingTrack ToTrack(bool playing, SpotifyClient.Item item) =>
        new(
            item.Name,
            item.Artists is null ? null : string.Join(", ", item.Artists.Select(a => a.Name)),
            item.Album?.Name,
            item.Album?.Images is { Count: > 0 } images ? images[0].Url : null,
            playing);
}
