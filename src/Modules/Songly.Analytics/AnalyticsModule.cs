using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Analytics.Counting;
using Songly.Analytics.Http;
using Songly.Analytics.Persistence;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.Social.Contracts;

namespace Songly.Analytics;

/// <summary>
/// Basic product analytics (original Claude.md §15, phase 2): aggregated counters of key domain events, from
/// the events other modules publish — no personal data, no per-user rows, no account-deletion handler.
/// </summary>
public sealed class AnalyticsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AnalyticsDbContext>();
        services.AddScoped<AnalyticsRecorder>();
        services.AddScoped<SummaryService>();

        services.AddScoped<CountDomainEvents>();
        services.AddScoped<IDomainEventHandler<UserRegistered>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<PostLiked>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<PostCommented>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<PostShared>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<FriendRequestSent>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<FriendRequestAccepted>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityMemberJoined>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<PostReported>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<PostReportDismissed>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityPostRemoved>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityCommentRemoved>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityMemberBanned>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealSubmitted>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealAccepted>>(provider => provider.GetRequiredService<CountDomainEvents>());
        services.AddScoped<IDomainEventHandler<CommunityBanAppealRejected>>(provider => provider.GetRequiredService<CountDomainEvents>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAnalyticsEndpoints();
}
