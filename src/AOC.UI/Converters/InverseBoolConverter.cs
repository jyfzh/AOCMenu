using Microsoft.UI.Xaml.Data;

namespace aoc.UI.Converters;

/// <summary>
/// Inverts a boolean value. Used for IsEnabled bindings that depend on
/// a negated IsLoading property.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string? language)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string? language)
    {
        if (value is bool b)
            return !b;
        return true;
    }
}
