using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.Notification.Http;
using Songly.Notification.Inbox;
using Songly.Notification.Persistence;
using Songly.Social.Contracts;

namespace Songly.Notification;

/// <summary>
/// The in-app inbox, filled from other modules' events — their activity and moderation decisions.
/// E-mail for moderation decisions and push come in later parts.
/// </summary>
public sealed class NotificationModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationDbContext>();
        services.AddScoped<NotificationService>();

        services.AddScoped<RecordActivityNotifications>();
        services.AddScoped<IDomainEventHandler<PostLiked>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<PostCommented>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<PostShared>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<FriendRequestSent>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<FriendRequestAccepted>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityMemberJoined>>(provider => provider.GetRequiredService<RecordActivityNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityMemberRoleChanged>>(provider => provider.GetRequiredService<RecordActivityNotifications>());

        services.AddScoped<RecordModerationNotifications>();
        services.AddScoped<IDomainEventHandler<CommunityMemberBanned>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealSubmitted>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealAccepted>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealRejected>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityBanEscalationAccepted>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityBanEscalationRejected>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityPostRemoved>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<CommunityCommentRemoved>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<PostReported>>(provider => provider.GetRequiredService<RecordModerationNotifications>());
        services.AddScoped<IDomainEventHandler<PostReportDismissed>>(provider => provider.GetRequiredService<RecordModerationNotifications>());

        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteNotificationsOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNotificationEndpoints();
}
