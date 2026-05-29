using System.Drawing;
using System.Windows;
using BlueMenu.App.Services;
using BlueMenu.App.ViewModels;
using Forms = System.Windows.Forms;

namespace BlueMenu.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new PersistenceService(), new MockBluetoothService());
        DataContext = _viewModel;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "BlueMenu"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open BlueMenu", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Click += (_, _) => ShowFromTray();

        IsVisibleChanged += (_, args) => _viewModel.IsVisible = (bool)args.NewValue;
        LocationChanged += (_, _) => _viewModel.OnWindowLocationChanged(Top, Left);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        };
        Closed += (_, _) => _notifyIcon.Dispose();

        var (top, left) = _viewModel.GetWindowPosition();
        if (top.HasValue && left.HasValue)
        {
            Top = top.Value;
            Left = left.Value;
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _viewModel.SaveState();
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }
}
