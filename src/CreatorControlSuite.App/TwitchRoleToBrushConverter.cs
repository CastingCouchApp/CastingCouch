using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CreatorControlSuite.App;

public sealed class TwitchRoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? string.Empty;

        if (text.Contains("[STREAMER]", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(255, 92, 92));
        }

        if (text.Contains("[MOD]", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(87, 214, 141));
        }

        if (text.Contains("[VIP]", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(232, 121, 249));
        }

        if (text.Contains("[SUB]", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(167, 139, 250));
        }

        return Application.Current.TryFindResource("TextPrimaryBrush") as Brush
               ?? Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
