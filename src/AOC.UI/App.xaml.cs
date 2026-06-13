using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using H.NotifyIcon;
using aoc.Infrastructure.IPC;
using aoc.UI.Services;

namespace aoc.UI;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        Window = new MainWindow();

        // Apply saved theme before the window renders
        ThemeService.Instance.Initialize();

        Window.Activate();
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        var showCommand = new RelayCommand(() =>
        {
            Window?.Activate();
            Window?.AppWindow.Show();
        });

        var exitCommand = new RelayCommand(async () =>
        {
            _trayIcon?.Dispose();

            // Send Shutdown RPC to ZeasnProxy so it exits immediately
            await ShutdownProxyAsync();

            Current.Exit();
        });

        var menu = new MenuFlyout();

        var showItem = new MenuFlyoutItem
        {
            Text = "显示主窗口",
            Command = showCommand,
            Icon = new SymbolIcon(Symbol.Setting),
        };
        menu.Items.Add(showItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem
        {
            Text = "退出",
            Command = exitCommand,
            Icon = new SymbolIcon(Symbol.Stop),
        };
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "aoc",
            LeftClickCommand = showCommand,
            ContextFlyout = menu,
        };

        // CRITICAL: TaskbarIcon relies on its Loaded event (which fires when added
        // to a visual tree) to call ForceCreate() -> TrayIcon.Create() ->
        // Shell_NotifyIcon(NIM_ADD). Since we create it programmatically (not in
        // XAML tree), the Loaded event never fires. We must call ForceCreate()
        // explicitly to register the native tray icon with the Windows shell.
        // Must call BEFORE setting Icon, because UpdateIcon uses NIM_MODIFY which
        // requires the icon to already be created via NIM_ADD.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);

        // Use Icon (System.Drawing.Icon) directly instead of IconSource (ImageSource)
        // to avoid async BitmapImage loading issues and ms-appx:// URI resolution
        // failures in unpackaged mode.
        var iconPath = ResolveIconPath();
        if (iconPath != null)
        {
            try
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            catch
            {
                SetFallbackTextIcon();
            }
        }
        else
        {
            SetFallbackTextIcon();
        }
    }

    /// <summary>
    /// Attempts to connect to the ZeasnProxy and send a Shutdown RPC,
    /// causing it to exit immediately instead of waiting for idle timeout.
    /// This is best-effort — the proxy may already be gone.
    /// </summary>
    private static async Task ShutdownProxyAsync()
    {
        try
        {
            await using var invoker = new ProxyClientInvoker();
            await invoker.ConnectAsync(TimeSpan.FromSeconds(2));
            invoker.SendShutdown();
        }
        catch
        {
            // Proxy may already be disconnected — that's fine.
        }
    }

    /// <summary>
    /// Resolves the AppIcon.ico path across possible deployment layouts
    /// (unpackaged root, MSIX AppX subdirectory).
    /// </summary>
    private static string? ResolveIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "AppIcon.ico"),
            Path.Combine(baseDir, "AppX", "Assets", "AppIcon.ico"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Fallback: generate a text-based icon when the .ico file cannot be found.
    /// </summary>
    private void SetFallbackTextIcon()
    {
        _trayIcon!.IconSource = new GeneratedIconSource
        {
            Text = "🖥️",
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.White),
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        };
    }

    private TaskbarIcon? _trayIcon;
}
