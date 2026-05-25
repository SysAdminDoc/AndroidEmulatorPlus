using System.Globalization;
using System.Windows.Data;

namespace AndroidEmulatorPlus.Views;

public sealed class BoolStatusConverter : IValueConverter
{
    public static readonly BoolStatusConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Installed" : "Missing";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
