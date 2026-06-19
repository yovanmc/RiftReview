using System.Text.Json;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;
using RiftReview.Core.Sync;
using Xunit;

public class DerivedMetricsBackfillTests
{
    [Fact]
    public void Backfills_rows_missing_derived_metrics_from_blobs()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");

        // A row stored WITHOUT derived metrics (nulls), but with real-shaped blobs.
        var parts = new List<ParticipantDto>
        {
            new("ME", 3, 103, 100, "MIDDLE", true, 5, 2, 3, 200, 10, 1000),
            new("B",  1, 110, 100, "TOP",    true, 3, 1, 1, 150, 0,  600),
            new("X",  8, 145, 200, "MIDDLE", false,4, 4, 4, 180, 0,  900),
        };
        var match = new MatchDto(new MatchMetadata("NA1_1", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
        var tl = new TimelineDto(new TimelineMetadata("NA1_1", new()),
            new TimelineInfo(60000, new List<FrameDto>
            {
                new(0, new(), new List<EventDto> { new("CHAMPION_KILL", 6*60000L, 8, 3) }), // 1 pre-15 death
            }));
        var json = new JsonSerializerOptions();
        var row = new MatchRow("NA1_1", 420, 1, 1800, "15.12.1", 103, "MIDDLE", true,
            5, 2, 3, 210, 80, 150, 8, 145, 100);   // derived metrics left null
        db.UpsertMatch(row, JsonSerializer.Serialize(match, json), JsonSerializer.Serialize(tl, json));

        int filled = DerivedMetricsBackfill.Run(db);

        Assert.Equal(1, filled);
        var back = db.GetMatch("NA1_1")!;
        Assert.Equal((5 + 3) / 8.0, back.KillParticipation!.Value, 3);  // teamKills = 5+3 = 8
        Assert.Equal(1000 / 1600.0, back.DamageShare!.Value, 3);        // teamDmg = 1000+600 = 1600
        Assert.Equal(1, back.DeathsPre15);
        Assert.Equal(0, DerivedMetricsBackfill.Run(db));                // idempotent: nothing left
    }
}
