namespace aoc.Domain;

public sealed record SettingDef(
    string Name,
    string Method,
    string Description,
    string Category,
    string? Getter = null,
    bool UseE2A0Profile = false,
    string? ReadProperty = null,
    string? ReadDeviceProperty = null,
    Dictionary<string, string>? EnumMap = null,
    object? ExtraArg = null,
    string? PrerequisiteSetting = null,
    string[]? PrerequisiteRequiredValues = null
)
{
    /// <summary>
    /// Pre-computed set of required prerequisite raw values for O(1) lookup.
    /// </summary>
    public FrozenSet<string>? PrerequisiteValueSet { get; } =
        PrerequisiteRequiredValues is { Length: >0 }
            ? PrerequisiteRequiredValues.ToFrozenSet(StringComparer.Ordinal)
            : null;
}
