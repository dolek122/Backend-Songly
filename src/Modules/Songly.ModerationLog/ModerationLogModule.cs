using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.ModerationLog.History;
using Songly.ModerationLog.Http;
using Songly.ModerationLog.Persistence;
using Songly.ModerationLog.Recording;

namespace Songly.ModerationLog;

/// <summary>
/// Append-only history of moderation decisions (original ADR 0024): community sanctions, appeals, reports,
/// moderator roles, suspensions and the administrator role. A module of its own, because analytics is by
/// definition without personal data (ADR 0004), and "what does this person have behind them" cuts across three
/// modules at once.
/// </summary>
public sealed class ModerationLogModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ModerationLogDbContext>();
        services.AddScoped<ModerationLogRecorder>();
        services.AddScoped<ModerationLogService>();

        services.AddScoped<RecordModerationDecisions>();
        services.AddScoped<IDomainEventHandler<CommunityMemberBanned>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanLifted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityPostRemoved>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityCommentRemoved>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<PostReportDismissed>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealSubmitted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealAccepted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealRejected>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealEscalated>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanEscalationAccepted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityBanEscalationRejected>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<CommunityMemberRoleChanged>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<UserSuspended>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<UserSuspensionLifted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<UserSuspensionAppealSubmitted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<UserSuspensionAppealAccepted>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<UserSuspensionAppealRejected>>(provider => provider.GetRequiredService<RecordModerationDecisions>());
        services.AddScoped<IDomainEventHandler<AdminRoleChanged>>(provider => provider.GetRequiredService<RecordModerationDecisions>());

        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, EraseModerationLogOnAccountDeletion>();
        services.AddScoped<IDomainEventHandler<CommunityDeleted>, EraseModerationLogOfDeletedCommunity>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapModerationLogEndpoints();
}
