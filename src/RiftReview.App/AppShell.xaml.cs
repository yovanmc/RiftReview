using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RiftReview.App.Views;
using RiftReview.Core.Analysis;
using Wpf.Ui.Controls;

namespace RiftReview.App;

public partial class AppShell : FluentWindow
{
    public AppShell(IServiceProvider sp, RiftReview.App.Services.NavigationService nav)
    {
        InitializeComponent();
        // Wire DI: NavigationView.SetServiceProvider resolves page instances from the DI container
        // when navigating to TargetPageType destinations.
        RootNavigation.SetServiceProvider(sp);
        nav.NavigationRequested += t => RootNavigation.Navigate(t, null);
        Loaded += OnLoaded;

        var health = sp.GetRequiredService<RiftReview.App.ViewModels.SessionHealthViewModel>();
        health.PropertyChanged += (_, _) => UpdateTiltBanner(health);
        UpdateTiltBanner(health);
        TiltBanner.MouseLeftButtonUp += (_, _) =>
            RootNavigation.Navigate(typeof(RiftReview.App.Views.SessionHealthView), null);
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
            "matchups"  => typeof(MatchupsView),
            "sessions"  => typeof(SessionHealthView),
            "climb"     => typeof(ClimbView),
            "settings"  => typeof(SettingsView),
            _ => typeof(ReviewView),
        };
        // Two-arg overload (Type, object?) — the single-arg Navigate(Type) does not exist on NavigationView.
        RootNavigation.Navigate(target, null);
    }

    private void UpdateTiltBanner(RiftReview.App.ViewModels.SessionHealthViewModel h)
    {
        TiltBanner.Visibility = h.BannerVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TiltBannerText.Text = h.BannerHeadline;
        var key = h.BannerSeverity == TiltSeverity.Tilted ? "TiltTintBrush" : "CautionTintBrush";
        TiltBanner.Background = (Brush)TiltBanner.Resources[key];
    }
}
