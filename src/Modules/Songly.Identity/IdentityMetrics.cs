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
/// Refresh outcomes <i>are</i> split, because they reveal nothing about other people's accounts.
/// <c>outcome="reuse"</c> stays at zero in healthy traffic — every increment is a sign of a
/// stolen refresh token.
/// </para>
/// </remarks>
internal sealed class IdentityMetrics
{
    private readonly Counter<long> _logins;
    private readonly Counter<long> _refreshes;

    public IdentityMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Songly.Identity");
        _logins = meter.CreateCounter<long>("songly.auth.login.attempts");
        _refreshes = meter.CreateCounter<long>("songly.auth.refresh.attempts");
    }

    public void LoginSucceeded() => _logins.Add(1, Outcome("success"));

    public void LoginFailed() => _logins.Add(1, Outcome("failure"));

    public void Refresh(RefreshOutcome outcome) => _refreshes.Add(1, Outcome(outcome.ToString().ToLowerInvariant()));

    private static KeyValuePair<string, object?> Outcome(string value) => new("outcome", value);
}
