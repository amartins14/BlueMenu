using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BlueMenu.App.Models;

namespace BlueMenu.App.Converters;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LogLevel level)
        {
            return Brushes.Gray;
        }

        return level switch
        {
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xCA, 0x8A, 0x04)),
            LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
