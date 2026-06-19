using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using Xunit;

public class SettingsStoreTests
{
    private static RiftReviewDb Db() => RiftReviewDb.Open("Data Source=:memory:");

    [Fact]
    public void Defaults_when_unset()
    {
        using var db = Db();
        var s = new SettingsStore(db);
        Assert.Equal(150, s.MatchDepth);
        Assert.True(s.DefaultRankedOnly);
    }

    [Fact]
    public void Persists_and_clamps_depth()
    {
        using var db = Db();
        new SettingsStore(db).MatchDepth = 5000;          // over max
        Assert.Equal(300, new SettingsStore(db).MatchDepth);   // clamped, persisted across instances
        new SettingsStore(db).MatchDepth = 1;             // under min
        Assert.Equal(20, new SettingsStore(db).MatchDepth);
    }

    [Fact]
    public void Persists_ranked_default()
    {
        using var db = Db();
        new SettingsStore(db).DefaultRankedOnly = false;
        Assert.False(new SettingsStore(db).DefaultRankedOnly);
    }

    [Fact]
    public void Trend_window_defaults_to_10_and_clamps()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var s = new SettingsStore(db);
        Assert.Equal(10, s.TrendWindow);
        s.TrendWindow = 3;   Assert.Equal(SettingsStore.MinTrendWindow, new SettingsStore(db).TrendWindow);
        s.TrendWindow = 999; Assert.Equal(SettingsStore.MaxTrendWindow, new SettingsStore(db).TrendWindow);
        s.TrendWindow = 14;  Assert.Equal(14, new SettingsStore(db).TrendWindow);
    }
}
