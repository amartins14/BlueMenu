using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using BlueMenu.App.Infrastructure;
using BlueMenu.App.Models;
using BlueMenu.App.Services;

namespace BlueMenu.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PersistenceService _persistence;
    private readonly IBluetoothService _bluetoothService;
    private readonly AppSettings _settings;

    private string? _globalError;
    private bool _isVisible;
    private bool _settingsVisible;

    public MainViewModel(PersistenceService persistence, IBluetoothService bluetoothService)
    {
        _persistence = persistence;
        _bluetoothService = bluetoothService;
        _settings = _persistence.Load();

        _bluetoothService.DeviceUpdated += OnDeviceUpdated;
        _bluetoothService.GlobalError += OnGlobalError;

        foreach (var device in _bluetoothService.GetInitialPairedDevices(_settings.DeviceAutoConnect))
        {
            UpsertDevice(device);
        }

        foreach (var cached in _settings.CachedDevices)
        {
            if (cached.State == BluetoothDeviceState.Discovered || IsLegacyMockCachedDevice(cached))
            {
                continue;
            }

            if (PairedDevices.Any(d => d.Id == cached.Id) || NearbyDevices.Any(d => d.Id == cached.Id))
            {
                continue;
            }

            UpsertDevice(new BluetoothDeviceItem
            {
                Id = cached.Id,
                Name = cached.Name,
                State = cached.State,
                LastSeenUtc = cached.LastSeenUtc,
                IsAutoConnectEnabled = _settings.DeviceAutoConnect.TryGetValue(cached.Id, out var enabled) && enabled
            });
        }

        ShowDebugLog = _settings.ShowDebugLog;
        ShowNotifications = _settings.ShowNotifications;
        OpenAtStartup = _settings.OpenAtStartup;
        RememberWindowPosition = _settings.RememberWindowPosition;

        ConnectCommand = new RelayCommand<BluetoothDeviceItem>(async device =>
        {
            if (device is null)
            {
                return;
            }

            AddLog(LogLevel.Info, $"Connect requested: {device.Name}");
            await _bluetoothService.ConnectAsync(device);
            SaveState();
        });

        DisconnectCommand = new RelayCommand<BluetoothDeviceItem>(async device =>
        {
            if (device is null)
            {
                return;
            }

            AddLog(LogLevel.Info, $"Disconnect requested: {device.Name}");
            await _bluetoothService.DisconnectAsync(device);
            SaveState();
        });

        PairCommand = new RelayCommand<BluetoothDeviceItem>(async device =>
        {
            if (device is null)
            {
                return;
            }

            AddLog(LogLevel.Info, $"Pair requested: {device.Name}");
            await _bluetoothService.PairAsync(device);
            SaveState();
        });

        ForgetCommand = new RelayCommand<BluetoothDeviceItem>(async device =>
        {
            if (device is null)
            {
                return;
            }

            AddLog(LogLevel.Warning, $"Forget requested: {device.Name}");
            await _bluetoothService.ForgetAsync(device);
            PairedDevices.Remove(device);
            NearbyDevices.Remove(device);
            SaveState();
        });

        ToggleSettingsCommand = new RelayCommand(() => SettingsVisible = !SettingsVisible);

        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);

        CopyLogsCommand = new RelayCommand(() =>
        {
            var payload = string.Join(Environment.NewLine, Logs.Select(l => $"[{l.Timestamp:O}] [{l.Level}] {l.Message}"));
            Clipboard.SetText(payload);
        });

        ShowErrorInfoCommand = new RelayCommand<BluetoothDeviceItem>(device =>
        {
            if (device is null || string.IsNullOrWhiteSpace(device.LastError))
            {
                return;
            }

            MessageBox.Show(device.LastError, "Device Error Details", MessageBoxButton.OK, MessageBoxImage.Information);
        });

        AddLog(LogLevel.Info, "BlueMenu initialized.");

        if (!_bluetoothService.IsAdapterAvailable)
        {
            GlobalError = "Bluetooth is not available on this device.";
        }
        else if (!IsRunningElevated)
        {
            GlobalError = "BlueMenu needs elevated permissions to manage Bluetooth.";
        }
    }

    public ObservableCollection<BluetoothDeviceItem> PairedDevices { get; } = [];

    public ObservableCollection<BluetoothDeviceItem> NearbyDevices { get; } = [];

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand PairCommand { get; }
    public ICommand ForgetCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ToggleVisibilityCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand ShowErrorInfoCommand { get; }

    public bool IsRunningElevated
        => WindowsIdentity.GetCurrent() is { } identity
           && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

    public string? GlobalError
    {
        get => _globalError;
        set => SetProperty(ref _globalError, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!SetProperty(ref _isVisible, value))
            {
                return;
            }

            if (value)
            {
                _bluetoothService.StartScan();
                AddLog(LogLevel.Info, "Auto search started (UI visible).");
            }
            else
            {
                _bluetoothService.StopScan();
                AddLog(LogLevel.Info, "Auto search stopped (UI hidden).");
            }
        }
    }

    public bool SettingsVisible
    {
        get => _settingsVisible;
        set => SetProperty(ref _settingsVisible, value);
    }

    public bool ShowDebugLog
    {
        get => _settings.ShowDebugLog;
        set
        {
            if (_settings.ShowDebugLog == value)
            {
                return;
            }

            _settings.ShowDebugLog = value;
            RaisePropertyChanged();
            SaveState();
        }
    }

    public bool RememberWindowPosition
    {
        get => _settings.RememberWindowPosition;
        set
        {
            if (_settings.RememberWindowPosition == value)
            {
                return;
            }

            _settings.RememberWindowPosition = value;
            RaisePropertyChanged();
            SaveState();
        }
    }

    public bool ShowNotifications
    {
        get => _settings.ShowNotifications;
        set
        {
            if (_settings.ShowNotifications == value)
            {
                return;
            }

            _settings.ShowNotifications = value;
            RaisePropertyChanged();
            SaveState();
        }
    }

    public bool OpenAtStartup
    {
        get => _settings.OpenAtStartup;
        set
        {
            if (_settings.OpenAtStartup == value)
            {
                return;
            }

            _settings.OpenAtStartup = value;
            RaisePropertyChanged();
            SaveState();
        }
    }

    public void OnWindowLocationChanged(double top, double left)
    {
        if (!RememberWindowPosition)
        {
            return;
        }

        _settings.WindowTop = top;
        _settings.WindowLeft = left;
        SaveState();
    }

    public (double? top, double? left) GetWindowPosition()
        => (_settings.WindowTop, _settings.WindowLeft);

    private void OnDeviceUpdated(BluetoothDeviceItem updatedDevice)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            updatedDevice.LastSeenUtc = DateTime.UtcNow;
            UpsertDevice(updatedDevice);

            if (!string.IsNullOrWhiteSpace(updatedDevice.LastError))
            {
                AddLog(LogLevel.Error, $"{updatedDevice.Name}: {updatedDevice.LastError}");
            }
            else
            {
                AddLog(LogLevel.Info, $"{updatedDevice.Name} -> {updatedDevice.State}");
            }

            SaveState();
        });
    }

    private void OnGlobalError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            GlobalError = message;
            AddLog(LogLevel.Warning, message);
        });
    }

    private void UpsertDevice(BluetoothDeviceItem device)
    {
        var existing = PairedDevices.Concat(NearbyDevices).FirstOrDefault(d => d.Id == device.Id);

        if (existing is null)
        {
            device.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(BluetoothDeviceItem.IsAutoConnectEnabled))
                {
                    _settings.DeviceAutoConnect[device.Id] = device.IsAutoConnectEnabled;
                    SaveState();
                }
            };

            AddToProperCollection(device);
            return;
        }

        existing.Name = device.Name;
        existing.State = device.State;
        existing.LastError = device.LastError;
        existing.LastSeenUtc = device.LastSeenUtc;

        if (existing.State == BluetoothDeviceState.Discovered)
        {
            MoveIfNeeded(existing, NearbyDevices);
        }
        else
        {
            MoveIfNeeded(existing, PairedDevices);
        }
    }

    private void AddToProperCollection(BluetoothDeviceItem device)
    {
        if (device.State == BluetoothDeviceState.Discovered)
        {
            NearbyDevices.Add(device);
        }
        else
        {
            PairedDevices.Add(device);
        }
    }

    private void MoveIfNeeded(BluetoothDeviceItem device, ObservableCollection<BluetoothDeviceItem> target)
    {
        if (target.Contains(device))
        {
            return;
        }

        PairedDevices.Remove(device);
        NearbyDevices.Remove(device);
        target.Add(device);
    }

    private void AddLog(LogLevel level, string message)
    {
        Logs.Insert(0, new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        });

        while (Logs.Count > 250)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    public void SaveState()
    {
        _settings.CachedDevices = PairedDevices
            .Select(d => new CachedDevice
            {
                Id = d.Id,
                Name = d.Name,
                State = d.State,
                LastSeenUtc = d.LastSeenUtc
            })
            .ToList();

        _persistence.Save(_settings);
    }

    private static bool IsLegacyMockCachedDevice(CachedDevice device)
        => device.Id.StartsWith("bm-", StringComparison.OrdinalIgnoreCase)
           || device.Id.StartsWith("near-", StringComparison.OrdinalIgnoreCase);
}
