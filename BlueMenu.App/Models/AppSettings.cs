namespace BlueMenu.App.Models;

public sealed class AppSettings
{
    public bool RememberWindowPosition { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    public bool OpenAtStartup { get; set; }

    public bool ShowDebugLog { get; set; } = true;

    public double? WindowTop { get; set; }

    public double? WindowLeft { get; set; }

    public Dictionary<string, bool> DeviceAutoConnect { get; set; } = new();

    public List<CachedDevice> CachedDevices { get; set; } = [];
}

public sealed class CachedDevice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required BluetoothDeviceState State { get; init; }

    public DateTime LastSeenUtc { get; init; }
}
