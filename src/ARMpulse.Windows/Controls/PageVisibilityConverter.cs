using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ARMpulse.Windows.Controls;

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int page && int.TryParse(parameter?.ToString(), out var expected) && page == expected ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
