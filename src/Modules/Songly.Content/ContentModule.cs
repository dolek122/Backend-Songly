using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Http;
using Songly.Content.Images;
using Songly.Content.Persistence;
using Songly.Content.Posts;
using Songly.Content.Reports;
using Songly.Identity.Contracts;

namespace Songly.Content;

/// <summary>Posts, likes, comments, shares, the feed, post images and reports to community moderation.</summary>
public sealed class ContentModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContentDbContext>();
        services.AddOptions<PostImageOptions>()
            .Bind(configuration.GetSection(PostImageOptions.Section))
            // A non-positive time-to-live would make every fresh upload "abandoned" and delete it
            // before the post that is about to use it.
            .Validate(
                o => o.MaxPending > 0 && o.PendingTtl > TimeSpan.Zero && o.CleanupBatchSize > 0 && o.CleanupInterval > TimeSpan.Zero,
                $"{PostImageOptions.Section}: MaxPending, PendingTtl, CleanupBatchSize and CleanupInterval must be positive")
            .ValidateOnStart();
        services.AddScoped<PostImageFiles>();
        services.AddScoped<PostImageUploads>();
        services.AddHostedService<AbandonedPostImageCleanupJob>();
        services.AddOptions<ReportOptions>()
            .Bind(configuration.GetSection(ReportOptions.Section))
            // A floor of zero would let a few dismissals take away for good the tool one could report
            // a moderator's own abuse with.
            .Validate(
                o => o.MinLimit >= 1 && o.Limit >= o.MinLimit && o.DismissalPenalty >= 0 && o.Window > TimeSpan.Zero && o.HistoryWindow > TimeSpan.Zero,
                $"{ReportOptions.Section}: MinLimit must be at least 1 and at most Limit, DismissalPenalty must not be negative, the windows must be positive")
            .ValidateOnStart();
        services.AddSingleton<ModerationMetrics>();
        services.AddScoped<ReporterHistory>();
        services.AddScoped<ReportService>();
        services.AddScoped<ContentVisibility>();
        services.AddScoped<PostViews>();
        services.AddScoped<PostService>();
        services.AddScoped<ReactionService>();
        services.AddScoped<CommentService>();
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteContentOnAccountDeletion>();
        services.AddScoped<IDomainEventHandler<CommunityDeleted>, DeletePostsOfDeletedCommunity>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapContentEndpoints();
}
