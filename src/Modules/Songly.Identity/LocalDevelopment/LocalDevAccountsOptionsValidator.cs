using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Songly.Common.Configuration;

namespace Songly.Identity.LocalDevelopment;

/// <summary>
/// The second barrier next to the setting being in <c>appsettings.Local.json</c> only: the local
/// accounts switched on by mistake where real secrets are required would be an administrator
/// account with a password published in the repository. The instance refuses to start instead of
/// relying on a warning in the README.
/// </summary>
internal sealed class LocalDevAccountsOptionsValidator(IConfiguration configuration) : IValidateOptions<LocalDevAccountsOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalDevAccountsOptions options) =>
        options.Enabled && configuration.GetValue<bool>(DevSecrets.ProductionRequiredSetting)
            ? ValidateOptionsResult.Fail(
                $"{LocalDevAccountsOptions.Section}:Enabled creates accounts with passwords from the repository, but this " +
                $"environment requires real secrets ({DevSecrets.ProductionRequiredSetting}=true). Turn the Local environment off.")
            : ValidateOptionsResult.Success;
}
