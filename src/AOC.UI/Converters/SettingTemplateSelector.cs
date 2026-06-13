using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using aoc.UI.ViewModels;

namespace aoc.UI.Converters;

/// <summary>
/// Selects the appropriate DataTemplate for a SettingViewModel based on
/// its value presentation type:
/// - HasEnumOptions → EnumSettingTemplate (ComboBox)
/// - HasNumericRange → SliderSettingTemplate (Slider + value label)
/// - Default         → ReadOnlySettingTemplate (read-only TextBlock)
/// </summary>
public sealed class SettingTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for enum-backed settings (ComboBox).</summary>
    public DataTemplate? EnumSettingTemplate { get; set; }

    /// <summary>Template for numeric-range settings (Slider).</summary>
    public DataTemplate? SliderSettingTemplate { get; set; }

    /// <summary>Fallback template for read-only display.</summary>
    public DataTemplate? ReadOnlySettingTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        if (item is SettingViewModel vm)
        {
            if (vm.HasEnumOptions)
                return EnumSettingTemplate!;
            if (vm.HasNumericRange)
                return SliderSettingTemplate!;
            return ReadOnlySettingTemplate!;
        }
        return ReadOnlySettingTemplate!;
    }
}
