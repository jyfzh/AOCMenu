namespace aoc.Domain;

public static class SettingCatalog
{
    public static readonly FrozenDictionary<string, SettingDef> All = new Dictionary<string, SettingDef>
    {
        // ── 图像 ──
        ["gamma"] = new("gamma", "SetGamma", "Gamma 值", "图像",
            EnumMap: new() { ["1"] = "0", ["2"] = "1", ["3"] = "2" },
            PrerequisiteSetting: "color-gamut", PrerequisiteRequiredValues: ["0"]),
        ["low-blue"] = new("low-blue", "SetLowBlueModel", "低蓝光模式", "图像",
            Getter: "LowBlueModel",
            EnumMap: new() { ["关闭"] = "0", ["多媒体"] = "1", ["网络"] = "2", ["办公室"] = "3", ["阅读"] = "4" },
            PrerequisiteSetting: "color-gamut", PrerequisiteRequiredValues: ["0"]),
        ["overdrive"] = new("overdrive", "SetOverDrive", "响应速度 OverDrive", "图像",
            EnumMap: new() { ["off"] = "0", ["weak"] = "1", ["medium"] = "2", ["strong"] = "3", ["strongest"] = "4" }),

        // ── 色彩 ──
        ["color-gamut"] = new("color-gamut", "SetColorGamut", "色彩空间", "色彩",
            ReadProperty: "EXT_OP_E2A0_20_ColorGamut",
            EnumMap: new() { ["standard"] = "0", ["srgb"] = "2" },
            PrerequisiteSetting: "hdr", PrerequisiteRequiredValues: ["0"]),

        // ── HDR ──
        ["hdr"] = new("hdr", "SetHDR", "HDR 模式", "HDR",
            Getter: "GetHDR",
            EnumMap: new() { ["关闭"] = "0", ["DisplayHDR"] = "1", ["图片"] = "2", ["电影"] = "3", ["游戏"] = "4" },
            PrerequisiteSetting: "color-gamut", PrerequisiteRequiredValues: ["0"]),

    }.ToFrozenDictionary();

    /// <summary>
    /// Reverse (value→key) enum maps, pre-computed for O(1) lookup.
    /// Keyed by setting key, maps raw SDK value string back to display name.
    /// Handles duplicate values by taking the first key for each SDK value
    /// (matching original FirstOrDefault semantics).
    /// </summary>
    public static readonly FrozenDictionary<string, FrozenDictionary<string, string>> ReverseEnumMaps = All
        .Where(kv => kv.Value.EnumMap is not null)
        .ToFrozenDictionary(
            kv => kv.Key,
            kv => kv.Value.EnumMap!
                .GroupBy(e => e.Value)
                .ToFrozenDictionary(g => g.Key, g => g.First().Key));

    /// <summary>
    /// Sorted comma-separated enum key lists for error messages, pre-computed.
    /// Keyed by setting key.
    /// </summary>
    public static readonly FrozenDictionary<string, string> EnumDisplayValues = All
        .Where(kv => kv.Value.EnumMap is not null)
        .ToFrozenDictionary(
            kv => kv.Key,
            kv => string.Join(", ", kv.Value.EnumMap!.Keys.OrderBy(x => x)));

    /// <summary>
    /// Looks up a setting by key. The key must already be lowercased
    /// (all settings are stored in lowercase).
    /// </summary>
    public static bool TryGet(string key, out SettingDef? def)
    {
        if (All.TryGetValue(key, out var found))
        {
            def = found;
            return true;
        }

        def = null;
        return false;
    }

    /// <summary>
    /// Gets a pre-computed reverse enum map for a setting key.
    /// Returns null if the setting has no enum map.
    /// </summary>
    public static FrozenDictionary<string, string>? GetReverseMap(string key)
    {
        if (ReverseEnumMaps.TryGetValue(key, out var map))
            return map;
        return null;
    }

    /// <summary>
    /// Gets a pre-computed comma-separated enum key list for error messages.
    /// Returns null if the setting has no enum map.
    /// </summary>
    public static string? GetEnumDisplayValues(string key)
    {
        if (EnumDisplayValues.TryGetValue(key, out var values))
            return values;
        return null;
    }
}
