using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot;
using RiftReview.Core.Sync;
using Wpf.Ui.Appearance;

namespace RiftReview.App;

public partial class App : Application
{
    private IHost? _host;
    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply WPF-UI dark theme + Hextech Gold accent BEFORE any window/control is created.
        ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0xC8, 0xAA, 0x6E),
            ApplicationTheme.Dark);

        bool seedDemo = e.Args.Contains("--seed-demo");

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiftReview");
        Directory.CreateDirectory(appData);
        var dbPath = seedDemo
            ? Path.Combine(appData, "demo.db")
            : Path.Combine(appData, "riftreview.db");
        if (seedDemo && File.Exists(dbPath)) File.Delete(dbPath); // fresh throwaway demo DB each run

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("appsettings.json", optional: false);
                cfg.AddUserSecrets<App>(optional: true);
            })
            .ConfigureServices((ctx, s) =>
            {
                s.Configure<RiotOptions>(ctx.Configuration.GetSection("Riot"));

                s.AddSingleton<ISystemClock, SystemClock>();
                s.AddSingleton<RiotRateLimiter>(sp =>
                    new RiotRateLimiter(sp.GetRequiredService<ISystemClock>()));

                s.AddHttpClient();

                s.AddSingleton<RiftReviewDb>(sp =>
                    RiftReviewDb.Open($"Data Source={dbPath}"));

                s.AddSingleton<RiotApiClient>(sp =>
                {
                    var o = sp.GetRequiredService<IOptions<RiotOptions>>().Value;
                    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                    return new RiotApiClient(
                        http,
                        sp.GetRequiredService<RiotRateLimiter>(),
                        o.ApiKey,
                        o.Platform);
                });
                s.AddSingleton<IRiotApiClient>(sp => sp.GetRequiredService<RiotApiClient>());

                s.AddSingleton<SyncService>(sp =>
                    new SyncService(
                        sp.GetRequiredService<RiftReviewDb>(),
                        sp.GetRequiredService<IRiotApiClient>()));

                s.AddSingleton<DataDragonClient>(sp =>
                    new DataDragonClient(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                        Path.Combine(appData, "ddragon")));

                s.AddSingleton<ViewModels.MainViewModel>();
                // MainWindow is the FluentWindow shell; MainViewModel is injected via its constructor.
                s.AddSingleton<MainWindow>();
            })
            .Build();

        if (seedDemo) Demo.DemoSeeder.Seed(Services.GetRequiredService<RiftReviewDb>());

        var win = Services.GetRequiredService<MainWindow>();
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
