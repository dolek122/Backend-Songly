using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Songly.IntegrationTests.Identity;

/// <summary>Ported from the original <c>JwtServiceClaimsTest</c> and <c>JwtServiceSecretTest</c>.</summary>
public sealed class AccessTokenTests(SonglyApiFactory factory)
{
    private const string DevSecret = "dev-only-secret-change-me-in-every-real-environment-32bytes+";
    private const string RealSecret = "Zq8v3Nf0a7LwXy2Pk5Rt9Bc4Hd6Jm1Qs8Vu0Ge3Ti7Ko";

    /// <summary>
    /// The token carries only what the server needs: the account id. An e-mail in it would travel
    /// with every request into proxy logs and crash reports, while nothing on the server reads it.
    /// </summary>
    [Fact]
    public async Task The_access_token_carries_the_user_id_and_no_email()
    {
        var user = await factory.CreateUserAsync("claims");
        var tokens = await factory.CreateClient().LoginAsync(user);

        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(tokens.AccessToken.Split('.')[1]));

        Assert.DoesNotContain("@", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("email", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(user.Id.ToString(), JsonDocument.Parse(payload).RootElement.GetProperty("sub").GetString());
    }

    /// <summary>
    /// With the public development key anyone could sign a token for any account, administrators
    /// included. An environment that requires real secrets must refuse to start with it — at
    /// startup, not at the first token.
    /// </summary>
    [Fact]
    public void The_app_refuses_to_start_with_the_dev_secret_where_real_secrets_are_required()
    {
        using var app = WithSecurity(DevSecret, productionSecretsRequired: true);

        var failure = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("Songly:Security:Jwt:Secret", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_secret_is_fine_locally()
    {
        using var app = WithSecurity(DevSecret, productionSecretsRequired: false);

        app.CreateClient().Dispose();
    }

    [Fact]
    public void A_real_secret_starts_where_real_secrets_are_required()
    {
        using var app = WithSecurity(RealSecret, productionSecretsRequired: true);

        app.CreateClient().Dispose();
    }

    /// <summary>HS256 needs at least 256 bits of key; a shorter one must fail at startup, not on the first login.</summary>
    [Fact]
    public void A_secret_shorter_than_32_bytes_is_rejected_at_startup()
    {
        using var app = WithSecurity("too-short", productionSecretsRequired: false);

        var failure = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("at least 32 bytes", failure.ToString(), StringComparison.Ordinal);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithSecurity(string secret, bool productionSecretsRequired) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("Songly:Security:Jwt:Secret", secret)
            .UseSetting("Songly:Security:ProductionSecretsRequired", productionSecretsRequired.ToString())
            .UseSetting("Database:MigrateOnStartup", "false"));
}
