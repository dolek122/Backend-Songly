using Songly.Common.Events;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.ModerationLog.Domain;

namespace Songly.ModerationLog.Recording;

/// <summary>
/// The module's only contact with the rest of the system: the same domain events the notification and analytics
/// modules react to (original Claude.md §2) — no reaching for the publishers' entities or services.
/// </summary>
/// <remarks>
/// The log inherits every volume limit for free, because the gates sit at the publishers: only a new ban
/// (original ADR 0014), only one appeal per case (ADR 0017, 0023), only an actual change of a role (ADR 0016,
/// 0021).
/// </remarks>
internal sealed class RecordModerationDecisions(ModerationLogRecorder recorder)
    : IDomainEventHandler<CommunityMemberBanned>,
      IDomainEventHandler<CommunityBanLifted>,
      IDomainEventHandler<CommunityPostRemoved>,
      IDomainEventHandler<CommunityCommentRemoved>,
      IDomainEventHandler<PostReportDismissed>,
      IDomainEventHandler<CommunityBanAppealSubmitted>,
      IDomainEventHandler<CommunityBanAppealAccepted>,
      IDomainEventHandler<CommunityBanAppealRejected>,
      IDomainEventHandler<CommunityBanAppealEscalated>,
      IDomainEventHandler<CommunityBanEscalationAccepted>,
      IDomainEventHandler<CommunityBanEscalationRejected>,
      IDomainEventHandler<CommunityMemberRoleChanged>,
      IDomainEventHandler<UserSuspended>,
      IDomainEventHandler<UserSuspensionLifted>,
      IDomainEventHandler<UserSuspensionAppealSubmitted>,
      IDomainEventHandler<UserSuspensionAppealAccepted>,
      IDomainEventHandler<UserSuspensionAppealRejected>,
      IDomainEventHandler<AdminRoleChanged>
{
    public Task HandleAsync(CommunityMemberBanned e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanIssued, e.CommunityId, e.UserId, e.ModeratorId, e.OccurredAt, expiresAt: e.ExpiresAt), cancellationToken);

    public Task HandleAsync(CommunityBanLifted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanLifted, e.CommunityId, e.UserId, e.ModeratorId, e.OccurredAt), cancellationToken);

    /// <summary>
    /// Recorded <b>twice, from two sides</b> (original ADR 0027): once as the decision about the author, with
    /// the number of reports the post had — its reports go with it, so this is the only moment they can be
    /// counted — and once in favour of <b>each reporter</b>.
    /// </summary>
    public Task HandleAsync(CommunityPostRemoved e, CancellationToken cancellationToken) =>
        recorder.RecordAllAsync(
            [
                new(ModerationAction.CommunityPostRemoved, e.CommunityId, e.PostAuthorId, e.ModeratorId, e.OccurredAt, reportCount: e.ReporterIds.Count),
                .. e.ReporterIds.Select(reporterId =>
                    new ModerationLogEntry(ModerationAction.PostReportUpheld, e.CommunityId, reporterId, e.ModeratorId, e.OccurredAt)),
            ],
            cancellationToken);

    public Task HandleAsync(CommunityCommentRemoved e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityCommentRemoved, e.CommunityId, e.CommentAuthorId, e.ModeratorId, e.OccurredAt), cancellationToken);

    /// <summary>The subject is the reporter, not the post's author: it is their report that was decided.</summary>
    public Task HandleAsync(PostReportDismissed e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.PostReportDismissed, e.CommunityId, e.ReporterId, e.ModeratorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanAppealSubmitted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanAppealSubmitted, e.CommunityId, e.UserId, e.UserId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanAppealAccepted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanAppealAccepted, e.CommunityId, e.UserId, e.ModeratorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanAppealRejected e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanAppealRejected, e.CommunityId, e.UserId, e.ModeratorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanAppealEscalated e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanAppealEscalated, e.CommunityId, e.UserId, e.UserId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanEscalationAccepted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanEscalationAccepted, e.CommunityId, e.UserId, e.AdminId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityBanEscalationRejected e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.CommunityBanEscalationRejected, e.CommunityId, e.UserId, e.AdminId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(CommunityMemberRoleChanged e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            new(e.ModerationGranted ? ModerationAction.CommunityModeratorGranted : ModerationAction.CommunityModeratorRevoked, e.CommunityId, e.UserId, e.ChangedBy, e.OccurredAt),
            cancellationToken);

    public Task HandleAsync(UserSuspended e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.AccountSuspended, null, e.UserId, e.ActorId, e.OccurredAt, expiresAt: e.ExpiresAt), cancellationToken);

    public Task HandleAsync(UserSuspensionLifted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.AccountSuspensionLifted, null, e.UserId, e.ActorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(UserSuspensionAppealSubmitted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.SuspensionAppealSubmitted, null, e.UserId, e.UserId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(UserSuspensionAppealAccepted e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.SuspensionAppealAccepted, null, e.UserId, e.ActorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(UserSuspensionAppealRejected e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(new(ModerationAction.SuspensionAppealRejected, null, e.UserId, e.ActorId, e.OccurredAt), cancellationToken);

    public Task HandleAsync(AdminRoleChanged e, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            new(e.Granted ? ModerationAction.PlatformAdminGranted : ModerationAction.PlatformAdminRevoked, null, e.UserId, e.ActorId, e.OccurredAt),
            cancellationToken);
}
