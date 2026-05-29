using BlueMenu.App.Infrastructure;

namespace BlueMenu.App.Models;

public sealed class BluetoothDeviceItem : ObservableObject
{
    private string _name = string.Empty;
    private BluetoothDeviceState _state;
    private bool _isAutoConnectEnabled;
    private string? _lastError;

    public required string Id { get; init; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public BluetoothDeviceState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public bool IsAutoConnectEnabled
    {
        get => _isAutoConnectEnabled;
        set => SetProperty(ref _isAutoConnectEnabled, value);
    }

    public string? LastError
    {
        get => _lastError;
        set
        {
            if (SetProperty(ref _lastError, value))
            {
                RaisePropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
