using RiftReview.Core.Analysis;
using Xunit;

namespace RiftReview.Core.Tests;

public class BuildAnalyzerTests
{
    // 4 matches for champ 103 role "MIDDLE":
    //   g1 win  [6655, 3157]
    //   g2 win  [6655, 3089]
    //   g3 loss [6655, 3157]
    //   g4 win  [3089]
    private static IReadOnlyList<MatchBuild> FourMatches() => new[]
    {
        new MatchBuild(103, "MIDDLE", Win: true,  CompletedItems: new[] { 6655, 3157 }),
        new MatchBuild(103, "MIDDLE", Win: true,  CompletedItems: new[] { 6655, 3089 }),
        new MatchBuild(103, "MIDDLE", Win: false, CompletedItems: new[] { 6655, 3157 }),
        new MatchBuild(103, "MIDDLE", Win: true,  CompletedItems: new[] { 3089 }),
    };

    [Theory]
    [InlineData(6655, 3, 2, 2.0 / 3.0)]
    [InlineData(3089, 2, 2, 1.0)]
    [InlineData(3157, 2, 1, 0.5)]
    public void Analyze_counts_games_wins_and_win_rate_per_item(int itemId, int games, int wins, double winRate)
    {
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", FourMatches());
        var item = best.Items.Single(i => i.ItemId == itemId);
        Assert.Equal(games, item.Games);
        Assert.Equal(wins, item.Wins);
        Assert.Equal(winRate, item.WinRate, precision: 10);
    }

    [Fact]
    public void Analyze_ordering_6655_then_3089_before_3157()
    {
        // 6655: Games=3 (highest) → first
        // 3089: Games=2, WinRate=1.0 → second (higher WR than 3157)
        // 3157: Games=2, WinRate=0.5 → third
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", FourMatches());
        Assert.Equal(3, best.Items.Count);
        Assert.Equal(6655, best.Items[0].ItemId);
        Assert.Equal(3089, best.Items[1].ItemId);
        Assert.Equal(3157, best.Items[2].ItemId);
    }

    [Fact]
    public void Analyze_topN_truncates()
    {
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", FourMatches(), topN: 2);
        Assert.Equal(2, best.Items.Count);
        Assert.Equal(6655, best.Items[0].ItemId);  // still top-2 by games
        Assert.Equal(3089, best.Items[1].ItemId);
    }

    [Fact]
    public void Analyze_empty_matches_returns_zero_games_and_empty_items()
    {
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", Array.Empty<MatchBuild>());
        Assert.Equal(0, best.TotalGames);
        Assert.Empty(best.Items);
    }

    [Fact]
    public void Analyze_returns_correct_champion_role_and_total_games()
    {
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", FourMatches());
        Assert.Equal(103, best.ChampionId);
        Assert.Equal("MIDDLE", best.Role);
        Assert.Equal(4, best.TotalGames);
    }

    [Fact]
    public void Analyze_dedupes_duplicate_item_in_same_game()
    {
        // If CompletedItems has a duplicate in one game (shouldn't happen upstream but defensive),
        // it should still count as 1 game for that item.
        var matches = new[]
        {
            new MatchBuild(103, "MIDDLE", Win: true, CompletedItems: new[] { 6655, 6655 }),
        };
        var best = BuildAnalyzer.Analyze(103, "MIDDLE", matches);
        var item = best.Items.Single(i => i.ItemId == 6655);
        Assert.Equal(1, item.Games);  // Distinct() applied in Analyze
    }
}
