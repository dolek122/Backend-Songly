namespace Songly.Community.Domain;

/// <summary>
/// The state of an appeal against a ban (original ADR 0017), with the second instance (ADR 0028).
/// No appeal is <c>null</c>, not a value. There is no "accepted" on either level: accepting an
/// appeal lifts the ban, so the row goes with the case.
/// </summary>
internal enum BanAppealStatus
{
    /// <summary>Waiting for the community's moderators.</summary>
    Pending,

    /// <summary>The moderators refused. Not the end any more: from here, and only from here, the case can go to the administration.</summary>
    Rejected,

    /// <summary>Waiting for the platform's administration.</summary>
    Escalated,

    /// <summary>The administration kept the ban — the end of the ladder.</summary>
    EscalationRejected,
}
