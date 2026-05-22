using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfChatClient.Converters;

/// <summary>
/// Returns Visible when the string is non-empty, Collapsed otherwise.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
