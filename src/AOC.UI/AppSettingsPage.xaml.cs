using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using aoc.Domain;
using aoc.UI.Services;
using aoc.UI.ViewModels;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace aoc.UI;

public sealed partial class AppSettingsPage : Page, INotifyPropertyChanged
{
    private ProxyService _proxy = null!; // set in OnNavigatedTo
    private CancellationTokenSource? _loadCts;

    public AppSettingsPage()
    {
        InitializeComponent();

        // Read app version from assembly
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = ver is not null ? $"版本 {ver.Major}.{ver.Minor}.{ver.Build}" : "开发版本";

        PipeName = "ZeasnProxy";
        UpdateConnectionStatus();

        // Initialize theme index from current setting
        _themeIndex = (int)ThemeService.Instance.CurrentTheme;
    }

    /// <summary>Current theme index (0=System, 1=Light, 2=Dark).</summary>
    private int _themeIndex;

    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex != value)
            {
                _themeIndex = value;
                OnPropertyChanged(nameof(ThemeIndex));
            }
        }
    }

    /// <summary>App version string</summary>
    public string AppVersion { get; }

    /// <summary>Named pipe name</summary>
    public string PipeName { get; }

    /// <summary>Connection status text</summary>
    public string ConnectionStatus { get; private set; } = "未连接";

    /// <summary>Connection indicator color</summary>
    public SolidColorBrush ConnectionColor { get; private set; } = new(Colors.Gray);

    // ── Monitor info properties ─────────────────────────────────────

    private MonitorInfo? _monitorInfo;

    /// <summary>Display name e.g. "AOC CU34G3X"</summary>
    public string MonitorDisplayName => _monitorInfo?.DisplayName ?? "未知显示器";

    /// <summary>Summary e.g. "CU34G3X | ~34.1" | 3440x1440"</summary>
    public string MonitorSummary => _monitorInfo?.Summary ?? "";

    /// <summary>True when monitor info has been loaded successfully</summary>
    public bool HasMonitorInfo => _monitorInfo is not null;

    /// <summary>True while loading monitor info</summary>
    public bool IsMonitorLoading { get; private set; }

    /// <summary>True when loading failed</summary>
    public bool HasMonitorError => !string.IsNullOrEmpty(MonitorErrorMessage) && !IsMonitorLoading;

    /// <summary>Error message if loading failed</summary>
    public string? MonitorErrorMessage { get; private set; }

    /// <summary>Formatted details text (monospace raw dump)</summary>
    public string? MonitorDetailsText { get; private set; }

    // ── Page lifecycle ──────────────────────────────────────────────

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainViewModel vm)
        {
            _proxy = vm.ProxyService;
            UpdateConnectionStatus();
            _ = LoadMonitorInfoAsync();
        }
        else
        {
            _proxy = new ProxyService();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loadCts?.Cancel();
    }

    // ── Monitor info loading ────────────────────────────────────────

    /// <summary>
    /// Fetches monitor info from the proxy and updates all related properties.
    /// </summary>
    private async Task LoadMonitorInfoAsync()
    {
        // Cancel any previous load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsMonitorLoading = true;
        MonitorErrorMessage = null;
        NotifyMonitorProperties();

        try
        {
            // Wait for proxy to be connected
            if (!_proxy.IsConnected)
            {
                MonitorErrorMessage = "代理未连接，无法读取显示器信息";
                NotifyMonitorProperties();
                return;
            }

            var info = await _proxy.GetMonitorInfoAsync();
            ct.ThrowIfCancellationRequested();

            if (info is null)
            {
                MonitorErrorMessage = "无法获取显示器信息 (SDK 返回空)";
                NotifyMonitorProperties();
                return;
            }

            _monitorInfo = info;
            MonitorDetailsText = FormatMonitorDetails(info);
            MonitorErrorMessage = null;
            Debug.WriteLine($"[AppSettingsPage] Monitor info loaded: {info.DisplayName}");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[AppSettingsPage] LoadMonitorInfoAsync cancelled.");
            return;
        }
        catch (Exception ex)
        {
            MonitorErrorMessage = $"读取显示器信息失败: {ex.Message}";
            Debug.WriteLine($"[AppSettingsPage] LoadMonitorInfoAsync error: {ex}");
        }
        finally
        {
            IsMonitorLoading = false;
            NotifyMonitorProperties();
        }
    }

    /// <summary>
    /// Formats the monitor info into a key-value dump matching the CLI output style.
    /// </summary>
    private static string FormatMonitorDetails(MonitorInfo info)
    {
        var sb = new StringBuilder();

        AppendField(sb, "err_code", info.ErrCode.ToString());
        AppendField(sb, "IsSucc", info.IsSucc ? "true" : "false");
        AppendField(sb, "err_msg", info.ErrMsg);
        AppendField(sb, "RequestId", info.RequestId);

        if (info.Tag is not null)
        {
            sb.AppendLine("Tag:");
            var tag = info.Tag;
            AppendField(sb, "    sManufacturer", tag.SManufacturer);
            AppendField(sb, "    sManufacturerDate", tag.SManufacturerDate);
            AppendField(sb, "    PlugAndPlayID", tag.PlugAndPlayID);
            AppendField(sb, "    sMonitorName", tag.SMonitorName);
            AppendField(sb, "    sSerialNumber", tag.SSerialNumber);
            AppendField(sb, "    sVersion", tag.SVersion);
            AppendField(sb, "    ScreenSize", tag.ScreenSize);
            AppendField(sb, "    TimingRecommandation", tag.TimingRecommandation);
            AppendField(sb, "    DisplayGamma", tag.DisplayGamma);
            AppendField(sb, "    DisplayTypeAndSignal", tag.DisplayTypeAndSignal);
            AppendField(sb, "    RedChromaticity", tag.RedChromaticity);
            AppendField(sb, "    GreenChromaticity", tag.GreenChromaticity);
            AppendField(sb, "    BlueChromaticity", tag.BlueChromaticity);
            AppendField(sb, "    WhitePoint", tag.WhitePoint);
        }
        else
        {
            AppendField(sb, "Tag", null);
        }

        AppendField(sb, "FunctionName", info.FunctionName);
        AppendField(sb, "CurrItem", info.CurrItem);

        return sb.ToString().TrimEnd();
    }

    private static void AppendField(StringBuilder sb, string key, string? value)
    {
        sb.Append(key);
        sb.Append(": ");
        sb.AppendLine(value ?? "(null)");
    }

    /// <summary>
    /// Notifies the UI of all monitor-related property changes.
    /// </summary>
    private void NotifyMonitorProperties()
    {
        OnPropertyChanged(nameof(MonitorDisplayName));
        OnPropertyChanged(nameof(MonitorSummary));
        OnPropertyChanged(nameof(HasMonitorInfo));
        OnPropertyChanged(nameof(IsMonitorLoading));
        OnPropertyChanged(nameof(HasMonitorError));
        OnPropertyChanged(nameof(MonitorErrorMessage));
        OnPropertyChanged(nameof(MonitorDetailsText));
    }

    // ── Connection status ──────────────────────────────────────────

    private void UpdateConnectionStatus()
    {
        var connected = IsProxyConnected();
        ConnectionStatus = connected ? "已连接" : "未连接";
        ConnectionColor = new SolidColorBrush(connected ? Colors.LimeGreen : Colors.Gray);
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionColor));
    }

    private static bool IsProxyConnected()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(
                        "ZeasnProxy_Running", out var handle))
                {
                    handle.Dispose();
                    return true;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    // ── Event handlers ─────────────────────────────────────────────

    private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedIndex < 0) return;

        var newTheme = (ElementTheme)ThemeCombo.SelectedIndex;
        if (newTheme == ThemeService.Instance.CurrentTheme) return;

        // Persist the preference
        ThemeService.Instance.SetTheme(newTheme);

        // Animate the transition
        var mainWindow = (MainWindow)App.Window;
        await mainWindow.AnimateThemeTransitionAsync(ThemeCombo, newTheme);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)App.Window;
        mainWindow.NavigationFrame.GoBack(new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        });
    }

    private void OnRefreshMonitorInfoClick(object sender, RoutedEventArgs e)
    {
        _ = LoadMonitorInfoAsync();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
