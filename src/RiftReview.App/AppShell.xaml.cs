using System;
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
    }
}
