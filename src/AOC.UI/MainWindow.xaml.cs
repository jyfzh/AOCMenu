using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using aoc.UI.Services;
using Windows.Foundation;

namespace aoc.UI;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// Exposed Frame for page navigation.
    /// </summary>
    public Frame NavigationFrame => RootFrame;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Size the window appropriately for a compact settings utility
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(640 * scale), (int)(720 * scale)));

        // Close → hide to tray instead of exiting
        Closed += OnWindowClosed;

        // Navigate to main page
        RootFrame.Navigate(typeof(MainPage));
    }

    /// <summary>
    /// Animates a theme transition with a circular reveal from a source element.
    /// The new-theme color expands from the element's center outward.
    /// </summary>
    public async Task AnimateThemeTransitionAsync(FrameworkElement source, ElementTheme newTheme)
    {
        // Calculate origin in the overlay's coordinate space
        GeneralTransform transform = source.TransformToVisual(ThemeOverlay);
        var origin = transform.TransformPoint(new Point(
            source.ActualSize.X / 2,
            source.ActualSize.Y / 2));

        // Window dimensions
        var w = ThemeOverlay.ActualSize.X;
        var h = ThemeOverlay.ActualSize.Y;

        if (w <= 0 || h <= 0)
        {
            // Fallback: just apply without animation
            ThemeService.Instance.ApplyCore(newTheme);
            return;
        }

        // Max distance from origin to any window corner (diameter needed)
        double maxDx = Math.Max(origin.X, w - origin.X);
        double maxDy = Math.Max(origin.Y, h - origin.Y);
        var maxRadius = Math.Sqrt(maxDx * maxDx + maxDy * maxDy);

        // Circle starts at 16×16 (radius 8), scale to cover farthest corner
        var maxScale = maxRadius / 8.0;

        // Set overlay color to match the NEW theme's background
        var bgColor = ThemeService.GetBackgroundColor(newTheme);
        RevealBrush.Color = bgColor;

        // Position circle center at click origin
        // 16×16 circle centered at origin: Translate to (origin.X - 8, origin.Y - 8)
        RevealTransform.TranslateX = origin.X - 8;
        RevealTransform.TranslateY = origin.Y - 8;

        // Reset scale to 0
        RevealTransform.ScaleX = 0;
        RevealTransform.ScaleY = 0;

        // Ensure overlay is visible
        ThemeOverlay.Opacity = 1.0;

        // ── Build storyboard ──
        var duration = new Duration(TimeSpan.FromMilliseconds(700));
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

        var scaleAnimX = new DoubleAnimation
        {
            From = 0, To = maxScale, Duration = duration, EasingFunction = ease,
        };
        Storyboard.SetTarget(scaleAnimX, RevealTransform);
        Storyboard.SetTargetProperty(scaleAnimX, "ScaleX");

        var scaleAnimY = new DoubleAnimation
        {
            From = 0, To = maxScale, Duration = duration, EasingFunction = ease,
        };
        Storyboard.SetTarget(scaleAnimY, RevealTransform);
        Storyboard.SetTargetProperty(scaleAnimY, "ScaleY");

        var opacityAnim = new DoubleAnimation
        {
            From = 1.0, To = 0.0, Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            BeginTime = TimeSpan.FromMilliseconds(650), // fade out in last 50ms
        };
        Storyboard.SetTarget(opacityAnim, ThemeOverlay);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleAnimX);
        storyboard.Children.Add(scaleAnimY);
        storyboard.Children.Add(opacityAnim);

        // Switch theme at ~50% of animation
        var themeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        themeTimer.Tick += (s, e) =>
        {
            themeTimer.Stop();
            ThemeService.Instance.ApplyCore(newTheme);
        };

        // Reset overlay after animation completes
        storyboard.Completed += (s, e) =>
        {
            ThemeOverlay.Opacity = 0.0;
            RevealTransform.ScaleX = 0;
            RevealTransform.ScaleY = 0;
        };

        // Go!
        themeTimer.Start();
        storyboard.Begin();

        await Task.Delay(800); // give animation time to play
    }

    private bool _isPinned;

    #region Always-on-top (Win32)

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private void SetAlwaysOnTop(bool top)
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        SetWindowPos(hwnd, top ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    #endregion

    private void OnPinButtonClick(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        SetAlwaysOnTop(_isPinned);
        PinIcon.Glyph = _isPinned ? "\uE77A" : "\uE718";
        ToolTipService.SetToolTip(PinButton, _isPinned ? "取消置顶" : "窗口置顶");
        AutomationProperties.SetName(PinButton, _isPinned ? "点击取消窗口置顶" : "点击设置窗口置顶");
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        AppWindow.Hide();
    }
}
