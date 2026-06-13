using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace aoc.UI;

public sealed partial class MainPage : Page
{
    private ViewModels.MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        // Cache page instance across back-navigation so we don't reconnect proxy
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (ViewModel.Categories.Count == 0)
            {
                await ViewModel.InitializeCommand.ExecuteAsync(null);
            }
        };
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)App.Window;
        mainWindow.NavigationFrame.Navigate(
            typeof(AppSettingsPage),
            ViewModel,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }

    // ViewModel lives as long as the cached page — no disposal on navigate-away.
    // Call DisconnectAsync() when the app exits to clean up the proxy.
}
