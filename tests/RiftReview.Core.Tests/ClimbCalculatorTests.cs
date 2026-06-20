using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using System.Collections.Generic;
using Xunit;

public class ClimbCalculatorTests
{
    private static MatchRow R(long start, bool win, int queue = 420) =>
        new("NA1_" + start, queue, start, 1800, "15.12", 103, "MIDDLE", win,
            5, 3, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Streaks_current_and_longest()
    {
        var ms = new List<MatchRow> { R(0, true), R(1, true), R(2, false), R(3, true), R(4, true), R(5, true) };
        var s = ClimbCalculator.Streaks(ms);
        Assert.Equal(3, s.CurrentStreak);
        Assert.Equal(3, s.LongestWinStreak);
        Assert.Equal(1, s.LongestLossStreak);
        Assert.Equal(6, s.RecentForm.Count);
        Assert.True(s.RecentForm[^1]);
    }

    [Fact]
    public void Streaks_current_negative_on_loss_streak()
    {
        var ms = new List<MatchRow> { R(0, true), R(1, false), R(2, false) };
        Assert.Equal(-2, ClimbCalculator.Streaks(ms).CurrentStreak);
    }

    [Fact]
    public void Streaks_empty()
    {
        var s = ClimbCalculator.Streaks(new List<MatchRow>());
        Assert.Equal(0, s.CurrentStreak);
        Assert.Empty(s.RecentForm);
    }

    [Fact]
    public void Segments_net_lp_and_games_in_window()
    {
        var snaps = new List<LpSnapshot>
        {
            new(100, "RANKED_SOLO_5x5", "GOLD", "IV", 30, 10, 8),
            new(200, "RANKED_SOLO_5x5", "GOLD", "III", 10, 13, 9),
        };
        var ms = new List<MatchRow> { R(150, true, 420), R(180, true, 420), R(180, true, 440) };
        var segs = ClimbCalculator.Segments(snaps, ms, "RANKED_SOLO_5x5");
        Assert.Single(segs);
        Assert.Equal(2, segs[0].GamesInWindow);
        Assert.Equal(80, segs[0].NetLp);
    }

    [Fact]
    public void Segments_need_two_snapshots()
    {
        var snaps = new List<LpSnapshot> { new(100, "RANKED_SOLO_5x5", "GOLD", "IV", 30, 10, 8) };
        Assert.Empty(ClimbCalculator.Segments(snaps, new List<MatchRow>(), "RANKED_SOLO_5x5"));
    }
}
