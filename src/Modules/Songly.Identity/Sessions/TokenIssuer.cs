using Microsoft.Extensions.Options;
using Songly.Identity.Domain;
using Songly.Identity.Http;
using Songly.Identity.Persistence;
using Songly.Identity.Security;

namespace Songly.Identity.Sessions;

/// <summary>
/// Issues a token pair and opens the session behind it. The one place a session starts: login
/// now, e-mail verification later. Two copies of this would drift apart on the first change to
/// the session lifecycle, and sessions are the part of this module that forgives no drift.
/// </summary>
internal sealed class TokenIssuer(IdentityDbContext db, AccessTokens accessTokens, IOptions<JwtOptions> options, TimeProvider time)
{
    public async Task<AuthTokensResponse> IssueForAsync(Guid userId, CancellationToken cancellationToken)
    {
        var refreshToken = SecureTokens.Generate();
        db.Sessions.Add(Session.Start(userId, SecureTokens.Hash(refreshToken), time.GetUtcNow(), options.Value.RefreshTokenTtl));
        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokensResponse(
            accessTokens.Issue(userId),
            refreshToken,
            "Bearer",
            (long)accessTokens.Lifetime.TotalSeconds);
    }
}
