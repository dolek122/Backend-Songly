namespace Songly.Identity.Domain;

/// <summary>
/// State of an appeal against a suspension (original ADR 0023). No appeal is <c>null</c>, not a
/// value — "may appeal" follows from an active suspension without one.
/// </summary>
/// <remarks>
/// There is no <c>Accepted</c>: accepting an appeal is lifting the suspension, so the state goes
/// away together with the case.
/// </remarks>
internal enum SuspensionAppealStatus
{
    Pending,
    Rejected,
}
