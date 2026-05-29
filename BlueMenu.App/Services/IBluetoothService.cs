using BlueMenu.App.Models;

namespace BlueMenu.App.Services;

public interface IBluetoothService
{
    event Action<BluetoothDeviceItem>? DeviceUpdated;

    event Action<string>? GlobalError;

    bool IsAdapterAvailable { get; }

    void StartScan();

    void StopScan();

    IReadOnlyList<BluetoothDeviceItem> GetInitialPairedDevices(Dictionary<string, bool> autoConnect);

    Task ConnectAsync(BluetoothDeviceItem device);

    Task DisconnectAsync(BluetoothDeviceItem device);

    Task PairAsync(BluetoothDeviceItem device);

    Task ForgetAsync(BluetoothDeviceItem device);
}
