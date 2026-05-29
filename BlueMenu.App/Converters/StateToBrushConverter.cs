using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BlueMenu.App.Models;

namespace BlueMenu.App.Converters;

public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not BluetoothDeviceState state)
        {
            return Brushes.Gray;
        }

        return state switch
        {
            BluetoothDeviceState.Connected => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            BluetoothDeviceState.Connecting => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            BluetoothDeviceState.Paired => new SolidColorBrush(Color.FromRgb(0x0E, 0x9F, 0x6E)),
            BluetoothDeviceState.Discovered => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
