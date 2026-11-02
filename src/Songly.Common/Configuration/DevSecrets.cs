namespace Songly.Common.Configuration;

/// <summary>
/// Refuses to start with a development secret where real secrets are required (original backend,
/// ADR 0044).
/// </summary>
/// <remarks>
/// <para>
/// <c>appsettings.json</c> ships default secrets so that nothing has to be configured locally.
/// They are public — they live in the repository — and every one of them contains
/// <see cref="Marker"/>. Without this check, a server where someone forgot to set the real value
/// would quietly start with the public one; for the JWT signing key that means anyone could sign a
/// token for any account, administrators included, and nothing would look wrong from outside.
/// </para>
/// <para>
/// An environment declares that it needs real secrets with
/// <c>Songly:Security:ProductionSecretsRequired=true</c>. This class knows nothing about the
/// domain; the module using a secret names the setting.
/// </para>
/// </remarks>
public static class DevSecrets
{
    public const string ProductionRequiredSetting = "Songly:Security:ProductionSecretsRequired";

    /// <summary>The marker every development secret in <c>appsettings.json</c> carries.</summary>
    public const string Marker = "dev-only";

    /// <returns>An error message when the value is a development secret that may not be used here; otherwise <c>null</c>.</returns>
    public static string? Reject(bool productionRequired, string? value, string setting) =>
        productionRequired && value is not null && value.Contains(Marker, StringComparison.Ordinal)
            ? $"{setting} has the development value from the repository, but this environment requires real secrets " +
              $"({ProductionRequiredSetting}=true). Set {setting} — the default value is public."
            : null;
}
