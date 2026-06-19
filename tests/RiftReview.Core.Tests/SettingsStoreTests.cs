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
}
