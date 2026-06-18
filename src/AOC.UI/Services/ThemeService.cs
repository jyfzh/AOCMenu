using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace aoc.UI.Services;

/// <summary>
/// Manages app theme (Light / Dark / Follow System) with preference persistence.
///
/// Uses a JSON file in %LOCALAPPDATA%\AOC.UI for storage instead of
/// Windows.Storage.ApplicationData, because ApplicationData.Current is not
/// reliably available during early startup in unpackaged WinUI 3 apps.
/// </summary>
public sealed class ThemeService
{
    private const string SettingsKey = "AppTheme";

    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AOC.UI", "theme.json");

    public static ThemeService Instance { get; } = new();

    /// <summary>Current active theme setting.</summary>
    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    /// <summary>Raised when the theme is about to change (before applying).</summary>
    public event Action<ElementTheme>? ThemeChanging;

    /// <summary>Raised after the theme has been applied.</summary>
    public event Action<ElementTheme>? ThemeChanged;

    // Private constructor — use Instance
    private ThemeService() { }

    /// <summary>
    /// Load saved preference and apply on startup.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var json = File.ReadAllText(StoragePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data is not null
                && data.TryGetValue(SettingsKey, out var saved)
                && Enum.TryParse<ElementTheme>(saved, out var theme))
            {
                CurrentTheme = theme;
            }
        }
        catch
        {
            // File doesn't exist or can't be read — use default theme.
        }
        ApplyCore(CurrentTheme);
    }

    /// <summary>
    /// Set a new theme and persist the preference.
    /// Does NOT apply immediately — caller handles the visual transition.
    /// </summary>
    public void SetTheme(ElementTheme theme)
    {
        if (CurrentTheme == theme) return;

        CurrentTheme = theme;
        ThemeChanging?.Invoke(theme);

        // Persist to JSON file
        Save();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            var data = new Dictionary<string, string>
            {
                [SettingsKey] = CurrentTheme.ToString()
            };
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
            // Best-effort persistence; failing to save a theme preference
            // is not worth crashing over.
        }
    }

    /// <summary>
    /// Directly applies theme to the root visual.
    /// </summary>
    public void ApplyCore(ElementTheme theme)
    {
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>
    /// Returns the effective theme color for a given ElementTheme setting.
    /// Resolves Default to actual system theme.
    /// </summary>
    public static ElementTheme GetEffective(ElementTheme theme)
    {
        if (theme != ElementTheme.Default) return theme;
        return Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;
    }

    /// <summary>Localized display name for a theme option.</summary>
    public static string GetDisplayName(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => "浅色",
        ElementTheme.Dark => "深色",
        ElementTheme.Default => "跟随系统",
        _ => "跟随系统",
    };

    /// <summary>Returns the approximate window background color for a theme.</summary>
    public static Windows.UI.Color GetBackgroundColor(ElementTheme theme)
    {
        var effective = GetEffective(theme);
        return effective == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1C)
            : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
    }
}
