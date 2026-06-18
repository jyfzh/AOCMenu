using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using aoc.Domain;
using aoc.UI.Services;

namespace aoc.UI.ViewModels;

/// <summary>
/// ViewModel for a single monitor setting, backed by a SettingDef.
/// Manages reading (get) and writing (set) via the proxy service.
/// </summary>
public partial class SettingViewModel : ObservableObject
{
    private readonly ProxyService _proxy;
    private readonly SettingDef _def;
    private bool _suppressSelectionEvents;

    /// <summary>
    /// Fires when a setting value has been successfully written.
    /// Key is the setting name (e.g. "color-gamut").
    /// MainViewModel subscribes to update dependent settings.
    /// </summary>
    public static event Action<string>? SettingValueChanged;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _currentValue = "---";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumericRange))]
    private int _sliderMax;

    [ObservableProperty]
    private double _sliderValue;

    private bool _suppressSliderEvents;

    public ObservableCollection<string> DisplayOptions { get; } = [];

    public bool HasEnumOptions => _def.EnumMap is not null;

    /// <summary>True when the backing SDK value reports a numeric range (MaxValue > 0).</summary>
    public bool HasNumericRange => SliderMax > 0;

    /// <summary>This setting depends on color-gamut being "standard".</summary>
    public bool HasPrerequisite => _def.PrerequisiteSetting is not null;

    /// <summary>
    /// True if this setting supports reading the current value.
    /// Checks for any read mechanism in the underlying SettingDef.
    /// </summary>
    public bool IsReadable => _def.Getter is not null
        || _def.ReadProperty is not null
        || _def.ReadDeviceProperty is not null;

    public bool IsToggleOn
    {
        get
        {
            if (SelectedIndex >= 0 && DisplayOptions.Count > SelectedIndex)
                return DisplayOptions[SelectedIndex] == "on";
            return false;
        }
    }

    /// <summary>The dependent setting key if this ViewModel has a prerequisite.</summary>
    public string? PrerequisiteSettingKey => _def.PrerequisiteSetting;

    public SettingViewModel(ProxyService proxy, SettingDef def)
    {
        _proxy = proxy;
        _def = def;
        _displayName = def.Description;

        if (def.EnumMap is not null)
        {
            foreach (var key in def.EnumMap.Keys)
                DisplayOptions.Add(key);
        }
    }

    /// <summary>
    /// Read the current value from the monitor, then update prerequsite state.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_proxy.IsConnected) return;

        IsLoading = true;
        StatusMessage = null;
        Debug.WriteLine($"[UI]     Read: {_def.Name} ({_def.Description})");
        try
        {
            var result = await _proxy.GetSettingAsync(_def.Name);
            if (result.Success)
            {
                CurrentValue = result.Value ?? "---";
                UpdateSelectedIndex(result.Value);
                UpdateSliderState(result);
                StatusMessage = null;
                Debug.WriteLine($"[UI]     Read OK: {_def.Name} = {result.Value}");
            }
            else
            {
                CurrentValue = "---";
                UpdateSliderState(result);
                // Non-readable settings are expected to fail — don't pollute the status bar
                if (IsReadable)
                    StatusMessage = result.UserMessage;
                Debug.WriteLine($"[UI]     Read FAIL: {_def.Name} — {result.UserMessage}");
            }
        }
        catch (Exception ex)
        {
            CurrentValue = "---";
            StatusMessage = $"读取失败: {ex.Message}";
            Debug.WriteLine($"[UI]     Read EXCEPTION: {_def.Name} — {ex.Message}");
        }
        finally
        {
            // After reading own value, check prerequisite state
            await CheckPrerequisiteAsync();
            IsLoading = false;
        }
    }

    /// <summary>
    /// Checks whether the prerequisite setting (e.g. color-gamut) is in an
    /// allowed state, and updates <see cref="IsEnabled"/> accordingly.
    /// </summary>
    public async Task CheckPrerequisiteAsync()
    {
        if (!HasPrerequisite)
        {
            IsEnabled = true;
            return;
        }

        try
        {
            var result = await _proxy.GetSettingAsync(_def.PrerequisiteSetting!);
            if (result.Success && result.Value is not null)
            {
                var met = _def.PrerequisiteValueSet?.Contains(result.Value) ?? true;
                IsEnabled = met;
                StatusMessage = met ? null : BuildPrerequisiteMessage();

                // When hardware forces a value (e.g. srgb → gamma=1, low-blue=off)
                // show the first option as default regardless of read-back result.
                if (!met && HasEnumOptions && DisplayOptions.Count > 0)
                {
                    _suppressSelectionEvents = true;
                    CurrentValue = DisplayOptions[0];
                    SelectedIndex = 0;
                    _suppressSelectionEvents = false;
                }
            }
            else
            {
                // Can't read prerequisite — allow the operation
                IsEnabled = true;
            }
        }
        catch
        {
            IsEnabled = true;
        }
    }

    /// <summary>
    /// Write a value to the monitor.
    /// </summary>
    public async Task SetValueAsync(string value)
    {
        if (!_proxy.IsConnected) return;

        // Check prerequisite before sending — avoids an IPC round-trip
        // for dependent settings when the user already sees IsEnabled=false.
        if (!IsEnabled && HasPrerequisite)
        {
            StatusMessage = BuildPrerequisiteMessage();
            return;
        }

        IsLoading = true;
        StatusMessage = null;
        Debug.WriteLine($"[UI]     Write: {_def.Name} = {value}");
        try
        {
            var result = await _proxy.SetSettingAsync(_def.Name, value);
            StatusMessage = result.Success ? "✅" : result.UserMessage;
            Debug.WriteLine($"[UI]     Write result: success={result.Success}");

            if (result.Success)
            {
                if (IsReadable)
                {
                    await RefreshAsync();
                }
                else
                {
                    // Write succeeded but setting doesn't support read-back.
                    // Optimistically update UI with the value just written.
                    CurrentValue = value;
                    _suppressSelectionEvents = true;
                    var idx = DisplayOptions.IndexOf(value);
                    if (idx >= 0)
                        SelectedIndex = idx;
                    _suppressSelectionEvents = false;
                    StatusMessage = "✅";
                }
                // Notify dependent settings (e.g. when color-gamut changes)
                SettingValueChanged?.Invoke(_def.Name);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"写入失败: {ex.Message}";
            Debug.WriteLine($"[UI]     Write EXCEPTION: {_def.Name} — {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Handle ComboBox selection change from the UI.
    /// Passes the display key to SetValueAsync so that SettingService.Set
    /// performs the EnumMap lookup (matching CLI behavior).
    /// </summary>
    partial void OnSelectedIndexChanged(int value)
    {
        if (_suppressSelectionEvents) return;
        if (value < 0 || value >= DisplayOptions.Count) return;
        if (_def.EnumMap is null) return;

        // Pass the display key (e.g. "srgb") — SettingService.Set resolves
        // it to the SDK value via EnumMap, matching the CLI path.
        var displayKey = DisplayOptions[value];
        var task = SetValueAsync(displayKey);
        _ = task.ContinueWith(static (t, _) =>
        {
            if (t.Exception is not null)
            {
                Debug.WriteLine($"[UI] OnSelectedIndexChanged: SetValueAsync failed: {t.Exception.InnerException?.Message}");
            }
        }, null, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Handle Slider value change from the UI.
    /// Rounds to nearest integer and passes the numeric string to SetValueAsync.
    /// Suppressed during programmatic updates to avoid feedback loops.
    /// </summary>
    partial void OnSliderValueChanged(double value)
    {
        if (_suppressSliderEvents) return;
        var intVal = (int)Math.Round(value);
        var task = SetValueAsync(intVal.ToString());
        _ = task.ContinueWith(static (t, _) =>
        {
            if (t.Exception is not null)
            {
                Debug.WriteLine($"[UI] OnSliderValueChanged: SetValueAsync failed: {t.Exception.InnerException?.Message}");
            }
        }, null, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Called by code-behind when the user toggles the switch.
    /// Passes the display key ("on"/"off") — SettingService.Set resolves
    /// it via EnumMap, matching the CLI path.
    /// </summary>
    public void OnToggleChanged(bool isOn)
    {
        if (_suppressSelectionEvents) return;
        if (_def.EnumMap is null) return;

        var target = isOn ? "on" : "off";
        var task = SetValueAsync(target);
        _ = task.ContinueWith(static (t, _) =>
        {
            if (t.Exception is not null)
            {
                Debug.WriteLine($"[UI] OnToggleChanged: SetValueAsync failed: {t.Exception.InnerException?.Message}");
            }
        }, null, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void UpdateSelectedIndex(string? rawValue)
    {
        if (_def.EnumMap is null || rawValue is null)
        {
            SelectedIndex = -1;
            return;
        }

        var reverseMap = SettingCatalog.GetReverseMap(_def.Name);
        if (reverseMap is not null && reverseMap.TryGetValue(rawValue, out var displayName))
        {
            var idx = DisplayOptions.IndexOf(displayName);
            if (idx >= 0)
            {
                _suppressSelectionEvents = true;
                SelectedIndex = idx;
                OnPropertyChanged(nameof(IsToggleOn));
                _suppressSelectionEvents = false;
                return;
            }
        }

        _suppressSelectionEvents = true;
        SelectedIndex = -1;
        OnPropertyChanged(nameof(IsToggleOn));
        _suppressSelectionEvents = false;
    }

    /// <summary>
    /// Builds a human-readable prerequisite-not-met message, e.g.
    /// "⚠️ 需要 色彩空间 为 standard" or "⚠️ 需要 HDR 模式 为 0".
    /// </summary>
    private string BuildPrerequisiteMessage()
    {
        if (_def.PrerequisiteSetting is null || _def.PrerequisiteValueSet is not { Count: >0 })
            return "";

        if (SettingCatalog.TryGet(_def.PrerequisiteSetting, out var prereqDef) && prereqDef is not null)
        {
            var prereqName = prereqDef.Description;
            var reverseMap = SettingCatalog.GetReverseMap(_def.PrerequisiteSetting);
            var values = string.Join(" / ", _def.PrerequisiteValueSet!.Select(v =>
                reverseMap is not null && reverseMap.TryGetValue(v, out var dn) ? dn : v));
            return $"⚠️ 需要 {prereqName} 为 {values}";
        }

        return "⚠️ 需要先调整依赖设置";
    }

    /// <summary>
    /// Updates slider state from a read result that has MaxValue.
    /// Called after a read to set up the numeric range slider.
    /// </summary>
    private void UpdateSliderState(OperationResult result)
    {
        if (result.MaxValue.HasValue && result.MaxValue.Value > 0)
        {
            SliderMax = result.MaxValue.Value;
            if (int.TryParse(result.Value, out var val))
            {
                _suppressSliderEvents = true;
                SliderValue = val;
                _suppressSliderEvents = false;
            }
        }
        else
        {
            SliderMax = 0;
        }
    }
}
