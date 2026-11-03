namespace Songly.Identity.LocalDevelopment;

internal sealed class LocalDevAccountsOptions
{
    public const string Section = "Songly:LocalDevAccounts";

    /// <summary>
    /// Off by default; only <c>appsettings.Local.json</c> turns it on — the <c>Local</c> environment
    /// is what the original called the <c>local</c> profile.
    /// </summary>
    public bool Enabled { get; set; }
}
