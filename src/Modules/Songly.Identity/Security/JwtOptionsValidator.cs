using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Songly.Common.Configuration;

namespace Songly.Identity.Security;

/// <summary>
/// Checked at startup, not at the first token: an instance that cannot sign tokens, or signs them
/// with the public development key, must not start at all.
/// </summary>
internal sealed class JwtOptionsValidator(IConfiguration configuration) : IValidateOptions<JwtOptions>
{
    private const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var setting = $"{JwtOptions.Section}:Secret";

        if (Encoding.UTF8.GetByteCount(options.Secret) < MinimumKeyBytes)
        {
            return ValidateOptionsResult.Fail($"{setting} must be at least {MinimumKeyBytes} bytes long");
        }

        var productionRequired = configuration.GetValue<bool>(DevSecrets.ProductionRequiredSetting);
        return DevSecrets.Reject(productionRequired, options.Secret, setting) is { } error
            ? ValidateOptionsResult.Fail(error)
            : ValidateOptionsResult.Success;
    }
}
