namespace Songly.Identity.Http;

/// <summary>
/// The appeal state as the API shows it. A copy of the domain enum, not the enum itself: endpoints
/// never see the persistence model (ADR 0002), and the mapping is one line.
/// </summary>
internal enum AppealStatus
{
    Pending,
    Rejected,
}
