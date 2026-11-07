namespace Songly.Notification.Push;

internal sealed class PushDeviceOptions
{
    public const string Section = "Songly:Notification:Push";

    /// <summary>
    /// How many devices of one account get pushes; the surplus is trimmed from the least recently
    /// registered. Without it the table grew with every reinstall, and every push also went to phones
    /// nobody has any more — FCM answers <c>UNREGISTERED</c> for them only after a while.
    /// </summary>
    public int MaxDevicesPerUser { get; set; } = 10;
}
