using Songly.Analytics;
using Songly.Common.Modules;
using Songly.Community;
using Songly.Content;
using Songly.Identity;
using Songly.Matching;
using Songly.ModerationLog;
using Songly.Notification;
using Songly.NowPlaying;
using Songly.Profile;
using Songly.Social;

namespace Songly.Api;

/// <summary>Every domain module the application is made of.</summary>
/// <remarks>
/// A module that exists but is missing here would compile and silently do nothing — the
/// architecture tests check that every <see cref="IModule"/> implementation is listed.
/// </remarks>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IdentityModule(),
        new ProfileModule(),
        new SocialModule(),
        new ContentModule(),
        new NowPlayingModule(),
        new MatchingModule(),
        new CommunityModule(),
        new NotificationModule(),
        new AnalyticsModule(),
        new ModerationLogModule(),
    ];
}
