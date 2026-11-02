using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Songly.Identity.LocalDevelopment;

namespace Songly.Identity.Tests;

/// <summary>
/// Ported from the original <c>LocalDevAccountsSeederTest.refusesToStart_whereProductionSecretsAreRequired</c>.
/// That the instance really does not start is tested in <c>LocalDevAccountsTests</c>; the message is
/// asserted here, where it is not racy.
/// </summary>
public sealed class LocalDevAccountsOptionsValidatorTests
{
    /// <summary>The Local environment switched on by mistake must not create an administrator with a known password.</summary>
    [Fact]
    public void Local_accounts_are_rejected_where_real_secrets_are_required()
    {
        var result = Validate(enabled: true, productionSecretsRequired: true);

        Assert.True(result.Failed);
        Assert.Contains("Turn the Local environment off", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_accounts_are_fine_locally() =>
        Assert.True(Validate(enabled: true, productionSecretsRequired: false).Succeeded);

    [Fact]
    public void Production_is_fine_without_local_accounts() =>
        Assert.True(Validate(enabled: false, productionSecretsRequired: true).Succeeded);

    private static ValidateOptionsResult Validate(bool enabled, bool productionSecretsRequired)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Songly:Security:ProductionSecretsRequired"] = productionSecretsRequired.ToString() })
            .Build();
        return new LocalDevAccountsOptionsValidator(configuration).Validate(null, new LocalDevAccountsOptions { Enabled = enabled });
    }
}
