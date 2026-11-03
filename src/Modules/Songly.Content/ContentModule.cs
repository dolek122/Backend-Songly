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
using Songly.Identity.Contracts;

namespace Songly.Content;

/// <summary>Posts, likes, comments, shares and the feed.</summary>
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
