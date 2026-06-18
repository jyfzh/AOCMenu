using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using aoc.Domain;
using aoc.UI.Services;

namespace aoc.UI.ViewModels;

/// <summary>
/// ViewModel for the main settings page.
/// Manages proxy lifecycle and groups settings by category.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProxyService _proxy = new();

    /// <summary>Exposes the proxy service for pages that need it.</summary>
    public ProxyService ProxyService => _proxy;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _statusMessage = "正在连接代理...";

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// Settings grouped by category. Each item is (CategoryName, Settings[]).
    /// </summary>
    public ObservableCollection<CategoryGroup> Categories { get; } = [];

    /// <summary>
    /// Initialize proxy connection and load all settings.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "正在连接代理...";
        Debug.WriteLine("[UI] InitializeAsync: starting...");

        try
        {
            // Start proxy and connect
            Debug.WriteLine("[UI] InitializeAsync: connecting to proxy...");
            await _proxy.ConnectAsync();
            StatusMessage = "正在初始化显示器...";

            Debug.WriteLine("[UI] InitializeAsync: initializing monitor...");
            var (initSuccess, initDiagnostic) = await _proxy.TryInitializeAsync();
            if (!initSuccess)
            {
                StatusMessage = $"显示器初始化失败: {initDiagnostic}";
                IsConnected = false;
                Debug.WriteLine($"[UI] InitializeAsync: init failed: {initDiagnostic}");
                return;
            }

            IsConnected = true;
            StatusMessage = "已连接";
            Debug.WriteLine("[UI] InitializeAsync: connected, loading settings...");

            // Group settings by category
            var groups = SettingCatalog.All.Values
                .GroupBy(s => s.Category)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var category = new CategoryGroup { Name = group.Key };
                foreach (var def in group)
                {
                    var vm = new SettingViewModel(_proxy, def);
                    category.Settings.Add(vm);
                }
                Categories.Add(category);
            }

            Debug.WriteLine($"[UI] InitializeAsync: {Categories.Sum(c => c.Settings.Count)} settings loaded.");

            // ── Subscribe to cross-setting change notifications (once only) ──
            SettingViewModel.SettingValueChanged -= OnSettingValueChanged; // guard against double-subscribe
            SettingViewModel.SettingValueChanged += OnSettingValueChanged;

            // Load initial values for all settings
            await LoadAllSettingsAsync();
            Debug.WriteLine("[UI] InitializeAsync: completed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UI] InitializeAsync: FAILED: {ex}");
            StatusMessage = $"连接失败: {ex.Message}";
            IsConnected = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// When a setting changes, refresh dependent settings whose prerequisite
    /// depends on the changed setting (e.g. color-gamut → gamma/hdr).
    /// </summary>
    private void OnSettingValueChanged(string settingKey)
    {
        // Find all settings that depend on this key
        foreach (var category in Categories)
        {
            foreach (var setting in category.Settings)
            {
                if (setting.HasPrerequisite
                    && string.Equals(setting.PrerequisiteSettingKey, settingKey, StringComparison.OrdinalIgnoreCase))
                {
                    // Full refresh — reads hardware-enforced value (for readable settings)
                    // and re-checks IsEnabled (via CheckPrerequisiteAsync in finally)
                    // Safe fire-and-forget: RefreshAsync has comprehensive try-catch inside.
                    var task = setting.RefreshCommand.ExecuteAsync(null);
                    _ = task.ContinueWith(static (t, _) =>
                    {
                        if (t.Exception is not null)
                        {
                            Debug.WriteLine($"[UI] OnSettingValueChanged: refresh failed: {t.Exception.InnerException?.Message}");
                        }
                    }, null, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }
    }

    /// <summary>
    /// Refresh all settings values.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await LoadAllSettingsAsync();
    }

    private async Task LoadAllSettingsAsync()
    {
        var allSettings = Categories.SelectMany(c => c.Settings).ToArray();
        Debug.WriteLine($"[UI] LoadAllSettingsAsync: refreshing {allSettings.Length} settings...");

        // Use a SemaphoreSlim to limit concurrent IPC requests.
        // Even though IPC itself serializes via _sendLock, limiting concurrency
        // reduces task overhead and avoids thread pool starvation when loading
        // many (50+) settings simultaneously.
        const int maxConcurrency = 8;
        using var throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = allSettings.Select(async setting =>
        {
            await throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                await setting.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UI] LoadAllSettingsAsync: setting '{setting.DisplayName}' failed: {ex.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        Debug.WriteLine("[UI] LoadAllSettingsAsync: completed.");
    }

    public async ValueTask DisposeAsync()
    {
        SettingViewModel.SettingValueChanged -= OnSettingValueChanged;
        await _proxy.DisposeAsync();
    }
}

/// <summary>
/// A group of settings under a category name (图像, 色彩, HDR).
/// </summary>
public class CategoryGroup
{
    public string Name { get; set; } = "";
    public ObservableCollection<SettingViewModel> Settings { get; } = [];
}
