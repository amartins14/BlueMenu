using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BlueMenu.App.Services;

public sealed class WindowsBluetoothService : IBluetoothService, IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    private readonly object _gate = new();
    private readonly Dictionary<string, BluetoothDeviceItem> _knownDevices = [];
    private readonly Dictionary<string, BluetoothLEDevice> _activeConnections = [];
    private readonly string[] _requestedProperties = [IsConnectedProperty];

    private DeviceWatcher? _watcher;
    private bool _disposed;

    public event Action<BluetoothDeviceItem>? DeviceUpdated;
    public event Action<string>? GlobalError;

    public bool IsAdapterAvailable { get; } = DetectBluetoothAdapter();

    public IReadOnlyList<BluetoothDeviceItem> GetInitialPairedDevices(Dictionary<string, bool> autoConnect)
    {
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = DeviceInformation.FindAllAsync(selector, _requestedProperties).AsTask().GetAwaiter().GetResult();
            var paired = new List<BluetoothDeviceItem>(devices.Count);

            lock (_gate)
            {
                _knownDevices.Clear();

                foreach (var info in devices)
                {
                    var item = ToDeviceItem(info, autoConnect);
                    _knownDevices[item.Id] = item;
                    paired.Add(Clone(item));
                }
            }

            return paired;
        }
        catch (Exception ex)
        {
            GlobalError?.Invoke($"Unable to read paired Bluetooth devices: {ex.Message}");
            return [];
        }
    }

    public void StartScan()
    {
        if (_disposed || !IsAdapterAvailable)
        {
            return;
        }

        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
        {
            return;
        }

        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(false);
        _watcher = DeviceInformation.CreateWatcher(selector, _requestedProperties, DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += OnWatcherAdded;
        _watcher.Updated += OnWatcherUpdated;
        _watcher.Removed += OnWatcherRemoved;
        _watcher.Start();
    }

    public void StopScan()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Added -= OnWatcherAdded;
        _watcher.Updated -= OnWatcherUpdated;
        _watcher.Removed -= OnWatcherRemoved;

        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }

        _watcher = null;
    }

    public async Task ConnectAsync(BluetoothDeviceItem device)
    {
        device.LastError = null;
        device.State = BluetoothDeviceState.Connecting;
        DeviceUpdated?.Invoke(Clone(device));

        try
        {
            if (!await EnsurePairedAsync(device))
            {
                return;
            }

            var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
            if (bleDevice is null)
            {
                SetError(device, "Unable to open Bluetooth device.");
                return;
            }

            var gattResult = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (gattResult.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
            {
                bleDevice.Dispose();
                SetError(device, "Connect failed while reading services.");
                return;
            }

            lock (_gate)
            {
                if (_activeConnections.Remove(device.Id, out var stale))
                {
                    stale.Dispose();
                }

                _activeConnections[device.Id] = bleDevice;

                if (_knownDevices.TryGetValue(device.Id, out var known))
                {
                    known.State = BluetoothDeviceState.Connected;
                    known.LastError = null;
                    known.LastSeenUtc = DateTime.UtcNow;
                }
            }

            device.State = BluetoothDeviceState.Connected;
            device.LastSeenUtc = DateTime.UtcNow;
            DeviceUpdated?.Invoke(Clone(device));
        }
        catch (Exception ex)
        {
            SetError(device, $"Connect failed: {ex.Message}");
        }
    }

    public Task DisconnectAsync(BluetoothDeviceItem device)
    {
        lock (_gate)
        {
            if (_activeConnections.Remove(device.Id, out var openDevice))
            {
                openDevice.Dispose();
            }

            if (_knownDevices.TryGetValue(device.Id, out var known))
            {
                known.State = BluetoothDeviceState.Disconnected;
                known.LastError = null;
                known.LastSeenUtc = DateTime.UtcNow;
            }
        }

        device.State = BluetoothDeviceState.Disconnected;
        device.LastError = null;
        device.LastSeenUtc = DateTime.UtcNow;
        DeviceUpdated?.Invoke(Clone(device));
        return Task.CompletedTask;
    }

    public async Task PairAsync(BluetoothDeviceItem device)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(device.Id);
            if (info is null)
            {
                SetError(device, "Pair failed: device not found.");
                return;
            }

            if (info.Pairing.IsPaired)
            {
                device.State = BluetoothDeviceState.Paired;
                device.LastError = null;
                device.LastSeenUtc = DateTime.UtcNow;
                DeviceUpdated?.Invoke(Clone(device));
                return;
            }

            if (!info.Pairing.CanPair)
            {
                SetError(device, "Pairing is not supported for this device.");
                return;
            }

            var result = await info.Pairing.PairAsync();
            if (result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
            {
                device.State = BluetoothDeviceState.Paired;
                device.LastError = null;
                device.LastSeenUtc = DateTime.UtcNow;

                lock (_gate)
                {
                    _knownDevices[device.Id] = Clone(device);
                }

                DeviceUpdated?.Invoke(Clone(device));
                return;
            }

            SetError(device, $"Pair failed: {result.Status}");
        }
        catch (Exception ex)
        {
            SetError(device, $"Pair failed: {ex.Message}");
        }
    }

    public async Task ForgetAsync(BluetoothDeviceItem device)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(device.Id);
            if (info?.Pairing.IsPaired == true)
            {
                var unpairResult = await info.Pairing.UnpairAsync();
                if (unpairResult.Status != DeviceUnpairingResultStatus.Unpaired)
                {
                    SetError(device, $"Unpair failed: {unpairResult.Status}");
                    return;
                }
            }

            lock (_gate)
            {
                _knownDevices.Remove(device.Id);
                if (_activeConnections.Remove(device.Id, out var openDevice))
                {
                    openDevice.Dispose();
                }
            }

            device.State = BluetoothDeviceState.Discovered;
            device.LastError = null;
            device.LastSeenUtc = DateTime.UtcNow;
            DeviceUpdated?.Invoke(Clone(device));
        }
        catch (Exception ex)
        {
            SetError(device, $"Forget failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopScan();

        lock (_gate)
        {
            foreach (var connection in _activeConnections.Values)
            {
                connection.Dispose();
            }

            _activeConnections.Clear();
            _knownDevices.Clear();
        }
    }

    private static bool DetectBluetoothAdapter()
    {
        try
        {
            var radios = Radio.GetRadiosAsync().AsTask().GetAwaiter().GetResult();
            return radios.Any(radio => radio.Kind == RadioKind.Bluetooth && radio.State != RadioState.Disabled);
        }
        catch
        {
            return false;
        }
    }

    private async void OnWatcherAdded(DeviceWatcher sender, DeviceInformation args)
    {
        await UpsertFromInfoAsync(args.Id, args);
    }

    private async void OnWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        await UpsertFromInfoAsync(args.Id, null);
    }

    private void OnWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        lock (_gate)
        {
            _knownDevices.Remove(args.Id);
        }
    }

    private async Task UpsertFromInfoAsync(string id, DeviceInformation? info)
    {
        try
        {
            info ??= await DeviceInformation.CreateFromIdAsync(id, _requestedProperties, DeviceInformationKind.AssociationEndpoint);
            if (info is null || string.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }

            BluetoothDeviceItem item;
            lock (_gate)
            {
                item = ToDeviceItem(info, []);

                if (_knownDevices.TryGetValue(item.Id, out var existing))
                {
                    existing.Name = item.Name;
                    existing.State = item.State;
                    existing.LastSeenUtc = DateTime.UtcNow;
                    existing.LastError = null;
                    item = Clone(existing);
                }
                else
                {
                    _knownDevices[item.Id] = item;
                    item = Clone(item);
                }
            }

            DeviceUpdated?.Invoke(item);
        }
        catch
        {
            // Ignore transient watcher refresh errors.
        }
    }

    private async Task<bool> EnsurePairedAsync(BluetoothDeviceItem device)
    {
        var info = await DeviceInformation.CreateFromIdAsync(device.Id);
        if (info is null)
        {
            SetError(device, "Device not found.");
            return false;
        }

        if (info.Pairing.IsPaired)
        {
            return true;
        }

        if (!info.Pairing.CanPair)
        {
            SetError(device, "Device must be paired before connecting.");
            return false;
        }

        var result = await info.Pairing.PairAsync();
        if (result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
        {
            return true;
        }

        SetError(device, $"Pairing failed: {result.Status}");
        return false;
    }

    private BluetoothDeviceItem ToDeviceItem(DeviceInformation info, Dictionary<string, bool> autoConnect)
    {
        var state = BluetoothDeviceState.Discovered;
        if (info.Pairing.IsPaired)
        {
            state = TryReadConnectedState(info) ? BluetoothDeviceState.Connected : BluetoothDeviceState.Paired;
        }

        return new BluetoothDeviceItem
        {
            Id = info.Id,
            Name = info.Name,
            State = state,
            LastSeenUtc = DateTime.UtcNow,
            IsAutoConnectEnabled = autoConnect.TryGetValue(info.Id, out var enabled) && enabled
        };
    }

    private static bool TryReadConnectedState(DeviceInformation info)
        => info.Properties.TryGetValue(IsConnectedProperty, out var value)
           && value is bool isConnected
           && isConnected;

    private void SetError(BluetoothDeviceItem device, string message)
    {
        device.State = BluetoothDeviceState.Disconnected;
        device.LastError = message;
        device.LastSeenUtc = DateTime.UtcNow;

        lock (_gate)
        {
            if (_knownDevices.TryGetValue(device.Id, out var known))
            {
                known.State = device.State;
                known.LastError = message;
                known.LastSeenUtc = device.LastSeenUtc;
            }
        }

        DeviceUpdated?.Invoke(Clone(device));
    }

    private static BluetoothDeviceItem Clone(BluetoothDeviceItem source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            State = source.State,
            LastSeenUtc = source.LastSeenUtc,
            IsAutoConnectEnabled = source.IsAutoConnectEnabled,
            LastError = source.LastError
        };
}
