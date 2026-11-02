using System.Diagnostics.Metrics;

namespace Songly.Identity;

/// <summary>
/// Counters of authentication outcomes, exported as <c>songly_auth_*</c> (original Claude.md §13).
/// </summary>
/// <remarks>
/// <para>
/// Login failures are deliberately <b>not</b> split by reason (unknown account vs wrong password):
/// that series would tell an attacker which addresses have an account. A spike of failures is all
/// it takes to notice brute force.
/// </para>
/// <para>
/// Refresh and registration outcomes <i>are</i> split, because they reveal nothing about other people's accounts.
/// <c>outcome="reuse"</c> stays at zero in healthy traffic — every increment is a sign of a
/// stolen refresh token.
/// </para>
/// </remarks>
internal sealed class IdentityMetrics
{
    private readonly Counter<long> _logins;
    private readonly Counter<long> _refreshes;
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _passwordResets;
    private readonly Counter<long> _accountDeletions;

    public IdentityMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Songly.Identity");
        _logins = meter.CreateCounter<long>("songly.auth.login.attempts");
        _refreshes = meter.CreateCounter<long>("songly.auth.refresh.attempts");
        _registrations = meter.CreateCounter<long>("songly.auth.registration.attempts");
        _passwordResets = meter.CreateCounter<long>("songly.auth.password-reset.attempts");
        _accountDeletions = meter.CreateCounter<long>("songly.auth.account-deletion.attempts");
    }

    public void LoginSucceeded() => _logins.Add(1, Outcome("success"));

    public void LoginFailed() => _logins.Add(1, Outcome("failure"));

    public void Refresh(RefreshOutcome outcome) => _refreshes.Add(1, Outcome(outcome.ToString().ToLowerInvariant()));

    /// <summary>
    /// <c>existing-account</c> means "someone registered with an address that already has an
    /// account" — without saying which. That series rising above its background level is a sign
    /// someone is sweeping addresses, even though the responses do not help them.
    /// </summary>
    public void Registration(string outcome) => _registrations.Add(1, Outcome(outcome));

    /// <summary>
    /// Deliberately no series for "no such account": like login, nothing may be read from these
    /// counters about which accounts exist. <c>throttled</c> rising faster than <c>requested</c>
    /// is enough to spot abuse.
    /// </summary>
    public void PasswordReset(string outcome) => _passwordResets.Add(1, Outcome(outcome));

    /// <summary>The web page's flow; like password reset, no series for "no such account".</summary>
    public void AccountDeletion(string outcome) => _accountDeletions.Add(1, Outcome(outcome));

    private static KeyValuePair<string, object?> Outcome(string value) => new("outcome", value);
}
