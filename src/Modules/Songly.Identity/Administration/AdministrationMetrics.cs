using System.Diagnostics.Metrics;

namespace Songly.Identity.Administration;

/// <summary>
/// <c>songly_admin_suspensions_total{action}</c> and <c>songly_admin_role_changes_total{action}</c>:
/// rare series in healthy traffic, where every increment is worth noticing — especially one nobody
/// expected (privilege escalation).
/// </summary>
internal sealed class AdministrationMetrics
{
    private readonly Counter<long> _suspensions;
    private readonly Counter<long> _roleChanges;

    public AdministrationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Songly.Identity.Administration");
        _suspensions = meter.CreateCounter<long>("songly.admin.suspensions");
        _roleChanges = meter.CreateCounter<long>("songly.admin.role.changes");
    }

    public void Suspension(string action) => _suspensions.Add(1, new KeyValuePair<string, object?>("action", action));

    public void RoleChange(string action) => _roleChanges.Add(1, new KeyValuePair<string, object?>("action", action));
}
