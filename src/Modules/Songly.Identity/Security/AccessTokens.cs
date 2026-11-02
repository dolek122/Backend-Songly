using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Songly.Identity.Security;

/// <summary>Issues access tokens and describes how they are validated.</summary>
internal sealed class AccessTokens(IOptions<JwtOptions> options, TimeProvider time)
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options = options.Value;

    public TimeSpan Lifetime => _options.AccessTokenTtl;

    /// <summary>
    /// Only the account id, in <c>sub</c> — no e-mail. The server never reads anything else from
    /// the token, and tokens end up in headers, proxy logs and client crash reports; every one of
    /// them used to carry the address too (original ADR 0046).
    /// </summary>
    public string Issue(Guid userId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now + _options.AccessTokenTtl,
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// No issuer or audience: one application issues and accepts the tokens. No clock skew: the
    /// lifetime is already short, and a tolerance would silently extend every token by it.
    /// </summary>
    public static TokenValidationParameters ValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        IssuerSigningKey = SigningKey(options),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };

    private static SymmetricSecurityKey SigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.Secret));
}
