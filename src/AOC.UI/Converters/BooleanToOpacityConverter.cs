using Microsoft.UI.Xaml.Data;

namespace aoc.UI.Converters;

/// <summary>
/// Converts a boolean to an opacity value: true → 1.0, false → 0.5.
/// Used to visually dim disabled settings cards that depend on color-gamut.
/// </summary>
public sealed class BooleanToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is bool b)
            return b ? 1.0 : 0.5;
        return 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        throw new NotSupportedException();
    }
}
