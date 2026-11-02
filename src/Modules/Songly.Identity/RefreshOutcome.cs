namespace Songly.Identity;

internal enum RefreshOutcome
{
    Success,
    Invalid,
    Expired,
    Reuse,
}
