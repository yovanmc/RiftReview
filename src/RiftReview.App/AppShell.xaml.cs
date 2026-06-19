using System;
using System.Windows;
using RiftReview.App.Views;
using Wpf.Ui.Controls;

namespace RiftReview.App;

public partial class AppShell : FluentWindow
{
    public AppShell(IServiceProvider sp)
    {
        InitializeComponent();
        // Wire DI: NavigationView.SetServiceProvider resolves page instances from the DI container
        // when navigating to TargetPageType destinations.
        RootNavigation.SetServiceProvider(sp);
        Loaded += OnLoaded;
    }

    // The NavigationView does not auto-navigate, so the content frame would be blank on launch.
    // Land on Review by default. A test-only "--page <Review|Champions|Settings>" arg selects the
    // initial page so the screenshot-verification gate can capture each page deterministically.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        var idx = Array.FindIndex(args, a => string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase));
        var page = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : "Review";
        Type target = page.ToLowerInvariant() switch
        {
            "champions" => typeof(ChampPoolView),
            "trends"    => typeof(TrendsView),
            "settings"  => typeof(SettingsView),
            _ => typeof(ReviewView),
        };
        // Two-arg overload (Type, object?) — the single-arg Navigate(Type) does not exist on NavigationView.
        RootNavigation.Navigate(target, null);
    }
}
