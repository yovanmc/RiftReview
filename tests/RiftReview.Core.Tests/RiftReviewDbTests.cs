using Microsoft.Data.Sqlite;
using RiftReview.Core.Data;
using Xunit;

public class RiftReviewDbTests
{
    private static RiftReviewDb NewDb() => RiftReviewDb.Open("Data Source=:memory:");

    [Fact]
    public void Initialize_sets_user_version_and_creates_tables()
    {
        using var db = NewDb();
        Assert.Equal(RiftReviewDb.LatestSchemaVersion, db.GetSchemaVersion());
        Assert.False(db.HasMatch("NA1_1"));
    }

    [Fact]
    public void Upsert_and_get_match_roundtrips()
    {
        using var db = NewDb();
        var row = new MatchRow("NA1_1", 420, 1_700_000_000, 1800, "15.12.1",
            103, "MIDDLE", true, 8, 3, 11, 210, 75, 412, 7, 157, 1_700_000_100);
        db.UpsertMatch(row, "{\"m\":1}", "{\"t\":1}");
        Assert.True(db.HasMatch("NA1_1"));
        var got = db.GetMatch("NA1_1")!;
        Assert.Equal(row, got);
        Assert.Equal("{\"t\":1}", db.GetTimelineJson("NA1_1"));
        Assert.Equal("{\"m\":1}", db.GetMatchJson("NA1_1"));
    }

    [Fact]
    public void RecentMatches_filters_by_ranked_and_orders_desc()
    {
        using var db = NewDb();
        db.UpsertMatch(Row("NA1_1", 420, start: 100), "{}", "{}");   // ranked solo
        db.UpsertMatch(Row("NA1_2", 400, start: 200), "{}", "{}");   // normal draft
        db.UpsertMatch(Row("NA1_3", 440, start: 300), "{}", "{}");   // ranked flex
        var ranked = db.RecentMatches(rankedOnly: true, limit: 20);
        Assert.Equal(new[] { "NA1_3", "NA1_1" }, ranked.Select(m => m.MatchId).ToArray());
        Assert.Equal(3, db.RecentMatches(rankedOnly: false, limit: 20).Count);
    }

    private static MatchRow Row(string id, int queue, long start) =>
        new(id, queue, start, 1800, "15.12.1", 103, "MIDDLE", true, 1, 1, 1, 100, 60, 0, 7, 1, start);

    [Fact]
    public void Schema_is_v3()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        Assert.Equal(3, db.GetSchemaVersion());
    }

    [Fact]
    public void Derived_metrics_roundtrip()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var row = new MatchRow("NA1_1", 420, 1, 1800, "15.12", 103, "MIDDLE", true,
            5, 3, 7, 200, 80, 150, 4, 8, 100,
            KillParticipation: 0.62, DamageShare: 0.27, DeathsPre15: 1);
        db.UpsertMatch(row, "{}", "{}");
        var back = db.GetMatch("NA1_1")!;
        Assert.Equal(0.62, back.KillParticipation!.Value, 3);
        Assert.Equal(0.27, back.DamageShare!.Value, 3);
        Assert.Equal(1, back.DeathsPre15);
    }

    [Fact]
    public void Lp_snapshot_insert_and_read_roundtrips()
    {
        using var db = NewDb();
        var snap = new LpSnapshot(1_700_000_000, "RANKED_SOLO_5x5", "GOLD", "II", 47, 120, 110);
        db.InsertLpSnapshot(snap);
        var all = db.GetLpSnapshots();
        Assert.Single(all);
        Assert.Equal(snap, all[0]);
    }

    [Fact]
    public void AllMatches_returns_every_stored_row_newest_first()
    {
        using var db = NewDb();
        for (int i = 0; i < 25; i++)
            db.UpsertMatch(Row($"NA1_{i}", i % 2 == 0 ? 420 : 400, start: 1000 + i), "{}", "{}");
        Assert.Equal(25, db.AllMatches(rankedOnly: false).Count);
        Assert.True(db.AllMatches(rankedOnly: true).Count < 25);     // only ranked (420/440)
        Assert.Equal("NA1_24", db.AllMatches(false)[0].MatchId);     // newest first
    }
}
