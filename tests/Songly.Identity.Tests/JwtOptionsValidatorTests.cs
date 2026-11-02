using Microsoft.Extensions.Configuration;
using Songly.Identity.Security;

namespace Songly.Identity.Tests;

/// <summary>
/// The startup check of the JWT signing key. Ported from the original <c>JwtServiceSecretTest</c>;
/// tested here, directly, because asserting the message of a failed start through
/// <c>WebApplicationFactory</c> is racy (see <c>AccessTokenTests</c>).
/// </summary>
public sealed class JwtOptionsValidatorTests
{
    private const string DevSecret = "dev-only-secret-change-me-in-every-real-environment-32bytes+";
    private const string RealSecret = "Zq8v3Nf0a7LwXy2Pk5Rt9Bc4Hd6Jm1Qs8Vu0Ge3Ti7Ko";

    /// <summary>
    /// With the public development key anyone could sign a token for any account, administrators
    /// included.
    /// </summary>
    [Fact]
    public void The_dev_secret_is_rejected_where_real_secrets_are_required()
    {
        var result = Validate(DevSecret, productionSecretsRequired: true);

        Assert.True(result.Failed);
        Assert.Contains("Songly:Security:Jwt:Secret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_secret_is_fine_locally() =>
        Assert.True(Validate(DevSecret, productionSecretsRequired: false).Succeeded);

    [Fact]
    public void A_real_secret_is_accepted_where_real_secrets_are_required() =>
        Assert.True(Validate(RealSecret, productionSecretsRequired: true).Succeeded);

    /// <summary>HS256 needs at least 256 bits of key — in UTF-8 bytes, not characters.</summary>
    [Theory]
    [InlineData("too-short")]
    [InlineData("0123456789012345678901234567890")]
    public void A_secret_shorter_than_32_bytes_is_rejected(string secret)
    {
        var result = Validate(secret, productionSecretsRequired: false);

        Assert.True(result.Failed);
        Assert.Contains("at least 32 bytes", result.FailureMessage, StringComparison.Ordinal);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string secret, bool productionSecretsRequired)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Songly:Security:ProductionSecretsRequired"] = productionSecretsRequired.ToString() })
            .Build();
        return new JwtOptionsValidator(configuration).Validate(null, new JwtOptions { Secret = secret });
    }
}
