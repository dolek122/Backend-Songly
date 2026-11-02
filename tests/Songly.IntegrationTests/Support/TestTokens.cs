using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Songly.IntegrationTests;

public static class TestTokens
{
    /// <summary>How a refresh token is stored: Base64 of its SHA-256.</summary>
    public static string HashOf(string refreshToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    /// <summary>
    /// An access token minted outside the application, for the cases the API never produces:
    /// expired, signed with another key, missing a subject.
    /// </summary>
    public static string Forge(SonglyApiFactory factory, string? subject, DateTime expires, string? secret = null)
    {
        secret ??= factory.Services.GetRequiredService<IConfiguration>()["Songly:Security:Jwt:Secret"]!;
        var claims = subject is null ? [] : new[] { new Claim(JwtRegisteredClaimNames.Sub, subject) };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = expires.AddMinutes(-15),
            NotBefore = expires.AddMinutes(-15),
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256),
        });
    }
}
