using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Http;
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
