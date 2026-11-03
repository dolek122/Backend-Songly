using Songly.Community.Domain;
using Songly.Community.Http;

namespace Songly.Community.Communities;

internal static class BanMapping
{
    public static BanAppealStatusDto? ToDto(BanAppealStatus? status) => status switch
    {
        null => null,
        BanAppealStatus.Pending => BanAppealStatusDto.Pending,
        BanAppealStatus.Rejected => BanAppealStatusDto.Rejected,
        BanAppealStatus.Escalated => BanAppealStatusDto.Escalated,
        BanAppealStatus.EscalationRejected => BanAppealStatusDto.EscalationRejected,
        _ => throw new InvalidOperationException($"Unknown appeal status {status}"),
    };
}
