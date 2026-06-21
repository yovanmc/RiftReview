using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

namespace RiftReview.Core.Tests;

public class TimelineExtractorPhaseTests
{
    // Build a minimal timeline: a frame per minute 0..lastMinute. csAt(minute) = my cumulative CS.
    // events: (minute, type, killerId, victimId, assists) appended to that minute's frame.
    private static TimelineDto Tl(int lastMinute, Func<int, int> csAt,
        params (int min, string type, int? killer, int? victim, int[] assists)[] events)
    {
        var frames = new List<FrameDto>();
        for (int m = 0; m <= lastMinute; m++)
        {
            var pf = new Dictionary<string, ParticipantFrameDto>
            {
                ["3"] = new(3, 0, csAt(m), 0),   // "me" = pid 3, gold unused (series passed in)
                ["8"] = new(8, 0, 0, 0),
            };
            var evts = events.Where(e => e.min == m)
                .Select(e => new EventDto(e.type, m * 60000L, e.killer, e.victim,
                    AssistingParticipantIds: e.assists.Length > 0 ? e.assists.ToList() : null))
                .ToList();
            frames.Add(new FrameDto(m * 60000L, pf, evts));
        }
        return new TimelineDto(new TimelineMetadata("T", new()), new TimelineInfo(60000, frames));
    }

    private static List<ChartPoint> Series(params (double min, double val)[] pts) =>
        pts.Select(p => new ChartPoint(p.min, p.val)).ToList();

    [Fact]
    public void Emits_only_phases_the_game_reached()
    {
        var ps24 = TimelineExtractor.BuildPhaseBreakdown(
            Tl(24, m => 0), 3, 100, Series((0, 0), (24, 0)));
        Assert.Equal(new[] { "Early", "Mid", "Late" }, ps24.Select(p => p.Label));
        Assert.Equal(24, ps24.Single(p => p.Label == "Late").EndMinute);

        var ps14 = TimelineExtractor.BuildPhaseBreakdown(
            Tl(14, m => 0), 3, 100, Series((0, 0), (14, 0)));
        Assert.Equal(new[] { "Early", "Mid" }, ps14.Select(p => p.Label));
        Assert.Equal(14, ps14.Single(p => p.Label == "Mid").EndMinute); // partial Mid
    }

    [Fact]
    public void Gold_delta_is_team_series_endpoint_difference()
    {
        var series = Series((0, 0), (10, 500), (20, -300), (24, -800));
        var ps = TimelineExtractor.BuildPhaseBreakdown(Tl(24, m => 0), 3, 100, series);
        Assert.Equal(500, ps.Single(p => p.Label == "Early").GoldDiffDelta);
        Assert.Equal(-800, ps.Single(p => p.Label == "Mid").GoldDiffDelta);   // -300 - 500
        Assert.Equal(-500, ps.Single(p => p.Label == "Late").GoldDiffDelta);  // -800 - (-300)
    }

    [Fact]
    public void Cs_per_minute_is_cumulative_delta_over_duration()
    {
        int Cs(int m) => m <= 10 ? 8 * m : m <= 20 ? 80 + 7 * (m - 10) : 150 + 5 * (m - 20);
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(24, Cs), 3, 100, Series((0, 0), (24, 0)));
        Assert.Equal(8.0, ps.Single(p => p.Label == "Early").CsPerMinute, 3);
        Assert.Equal(7.0, ps.Single(p => p.Label == "Mid").CsPerMinute, 3);
        Assert.Equal(5.0, ps.Single(p => p.Label == "Late").CsPerMinute, 3); // (170-150)/4
    }

    [Fact]
    public void Death_on_phase_boundary_counts_in_the_later_phase()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(12, m => 0, (10, "CHAMPION_KILL", 8, 3, new int[0])),
            3, 100, Series((0, 0), (12, 0)));
        Assert.Equal(0, ps.Single(p => p.Label == "Early").Deaths);
        Assert.Equal(1, ps.Single(p => p.Label == "Mid").Deaths);
    }

    [Fact]
    public void Kill_participation_uses_enemy_deaths_as_denominator()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0,
                (4, "CHAMPION_KILL", 3, 8, new int[0]),      // my kill (enemy 8 dies)
                (6, "CHAMPION_KILL", 1, 7, new[] { 3 }),     // my assist (enemy 7 dies)
                (8, "CHAMPION_KILL", 2, 9, new int[0]),      // teammate kill, not me
                (5, "CHAMPION_KILL", 8, 3, new int[0])),     // I die — not a team kill
            3, 100, Series((0, 0), (9, 0)));
        var early = ps.Single(p => p.Label == "Early");
        Assert.Equal(3, early.TeamKills);     // enemies 8,7,9 died
        Assert.Equal(1, early.Kills);
        Assert.Equal(1, early.Assists);
        Assert.Equal(2.0 / 3.0, early.KillParticipation!.Value, 3);
        Assert.Equal(1, early.Deaths);
    }

    [Fact]
    public void Kill_participation_is_null_when_team_scored_no_kills()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0, (5, "CHAMPION_KILL", 8, 3, new int[0])), // only my death
            3, 100, Series((0, 0), (9, 0)));
        Assert.Null(ps.Single(p => p.Label == "Early").KillParticipation);
        Assert.Equal(0, ps.Single(p => p.Label == "Early").TeamKills);
    }

    [Fact]
    public void Deaths_while_behind_uses_team_gold_sign_at_death()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0,
                (5, "CHAMPION_KILL", 8, 3, new int[0]),   // behind
                (8, "CHAMPION_KILL", 8, 3, new int[0])),  // ahead
            3, 100, Series((0, 0), (5, -200), (8, 300)));
        var early = ps.Single(p => p.Label == "Early");
        Assert.Equal(2, early.Deaths);
        Assert.Equal(1, early.DeathsWhileBehind);
    }
}
