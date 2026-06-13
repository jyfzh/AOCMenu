using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace aoc.UI.Converters;

/// <summary>
/// Converts a boolean to Visibility: true → Visible, false → Collapsed.
/// Used with {x:Bind} in DataTemplate for conditional element visibility.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        if (value is Visibility v)
            return v == Visibility.Visible;
        return true;
    }
}
