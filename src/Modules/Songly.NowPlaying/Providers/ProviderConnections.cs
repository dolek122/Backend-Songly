using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.NowPlaying.Persistence;
using Songly.NowPlaying.Status;
using Songly.NowPlaying.Stream;
using StackExchange.Redis;

namespace Songly.NowPlaying.Providers;

/// <summary>Connecting and disconnecting a music service: the OAuth round trip through the browser.</summary>
/// <remarks>
/// The browser comes back to the callback from the provider's redirect without our JWT, so the
/// callback is public and identifies the user by a one-time <c>state</c>: random, kept in Redis with
/// a short lifetime, and used up by the first callback that brings it — whatever its result.
/// </remarks>
internal sealed class ProviderConnections(
    IConnectionMultiplexer redis,
    IMusicProvider provider,
    NowPlayingDbContext db,
    StatusStore statuses,
    StatusPublisher publisher,
    IOptions<NowPlayingOptions> options)
{
    private const string StateKeyPrefix = "now-playing:oauth-state:";

    public async Task<string> BeginAsync(Guid userId)
    {
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        await redis.GetDatabase().StringSetAsync(StateKeyPrefix + state, userId.ToString(), options.Value.OAuthStateTtl);
        return provider.AuthorizeUrl(state);
    }

    public async Task CompleteAsync(string state, string authorizationCode, CancellationToken cancellationToken)
    {
        var userId = await redis.GetDatabase().StringGetDeleteAsync(StateKeyPrefix + state);
        if (userId.IsNullOrEmpty)
        {
            throw new InvalidOAuthStateException();
        }

        await provider.ConnectAsync(Guid.Parse(userId.ToString()), authorizationCode, cancellationToken);
    }

    /// <summary>The provider came back without a code (consent refused, or its error). The state is used up at once, so it cannot be put into the callback again for the rest of its lifetime.</summary>
    public Task AbandonAsync(string state) => redis.GetDatabase().KeyDeleteAsync(StateKeyPrefix + state);

    /// <summary>
    /// Idempotent. A manual status stays — it does not depend on the provider — and while it does, the
    /// user still "plays" something, so "stopped listening" would be a lie.
    /// </summary>
    public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.Links.Where(l => l.UserId == userId && l.Provider == provider.Name).ExecuteDeleteAsync(cancellationToken);
        await statuses.ForgetProviderAsync(userId);
        if (await statuses.ManualAsync(userId) is null)
        {
            await publisher.PublishStoppedAsync(userId);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
