using System.Windows.Threading;
using BlueMenu.App.Models;

namespace BlueMenu.App.Services;

public sealed class MockBluetoothService : IBluetoothService
{
    private readonly Random _random = new();
    private readonly DispatcherTimer _scanTimer;
    private readonly List<BluetoothDeviceItem> _knownDevices = [];

    public MockBluetoothService()
    {
        _scanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _scanTimer.Tick += (_, _) => DiscoverNearbyDevice();
    }

    public event Action<BluetoothDeviceItem>? DeviceUpdated;
    public event Action<string>? GlobalError;

    public bool IsAdapterAvailable => true;

    public IReadOnlyList<BluetoothDeviceItem> GetInitialPairedDevices(Dictionary<string, bool> autoConnect)
    {
        _knownDevices.Clear();
        var items = new[]
        {
            new BluetoothDeviceItem { Id = "bm-1001", Name = "BlueBuds Pro", State = BluetoothDeviceState.Connected },
            new BluetoothDeviceItem { Id = "bm-1002", Name = "Work Headset", State = BluetoothDeviceState.Disconnected },
            new BluetoothDeviceItem { Id = "bm-1003", Name = "Travel Earbuds", State = BluetoothDeviceState.Paired }
        };

        foreach (var item in items)
        {
            item.IsAutoConnectEnabled = autoConnect.TryGetValue(item.Id, out var enabled) && enabled;
            _knownDevices.Add(item);
        }

        return _knownDevices.ToList();
    }

    public void StartScan()
    {
        _scanTimer.Start();
    }

    public void StopScan()
    {
        _scanTimer.Stop();
    }

    public async Task ConnectAsync(BluetoothDeviceItem device)
    {
        device.LastError = null;
        device.State = BluetoothDeviceState.Connecting;
        DeviceUpdated?.Invoke(device);

        await Task.Delay(900);
        if (_random.Next(0, 10) < 2)
        {
            SetError(device, "Failed to connect: radio busy.", "Try again in a moment.");
            return;
        }

        device.State = BluetoothDeviceState.Connected;
        DeviceUpdated?.Invoke(device);
    }

    public Task DisconnectAsync(BluetoothDeviceItem device)
    {
        device.State = BluetoothDeviceState.Disconnected;
        device.LastError = null;
        DeviceUpdated?.Invoke(device);
        return Task.CompletedTask;
    }

    public async Task PairAsync(BluetoothDeviceItem device)
    {
        await Task.Delay(700);

        if (_random.Next(0, 10) < 3)
        {
            SetError(device, "Failed to Pair", "Make sure the device is in pairing mode and close to this PC.");
            return;
        }

        device.LastError = null;
        device.State = BluetoothDeviceState.Paired;
        DeviceUpdated?.Invoke(device);
    }

    public Task ForgetAsync(BluetoothDeviceItem device)
    {
        _knownDevices.RemoveAll(d => d.Id == device.Id);
        device.State = BluetoothDeviceState.Discovered;
        DeviceUpdated?.Invoke(device);
        return Task.CompletedTask;
    }

    private void DiscoverNearbyDevice()
    {
        if (_random.Next(0, 50) == 1)
        {
            GlobalError?.Invoke("Bluetooth radio is busy. Try again in a moment.");
            return;
        }

        var discovered = new BluetoothDeviceItem
        {
            Id = $"near-{_random.Next(1000, 9999)}",
            Name = $"Nearby Buds {_random.Next(1, 99)}",
            State = BluetoothDeviceState.Discovered,
            LastSeenUtc = DateTime.UtcNow
        };

        _knownDevices.Add(discovered);
        DeviceUpdated?.Invoke(discovered);
    }

    private void SetError(BluetoothDeviceItem device, string title, string suggestion)
    {
        device.State = BluetoothDeviceState.Disconnected;
        device.LastError = $"{title}: {suggestion}";
        DeviceUpdated?.Invoke(device);
    }
}
