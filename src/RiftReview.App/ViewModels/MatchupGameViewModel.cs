using System;
using RiftReview.Core.Data;

namespace RiftReview.App.ViewModels;

// One game inside a matchup's games list; clicking it opens its deep-dive.
public sealed class MatchupGameViewModel
{
    private readonly MatchRow _row;
    public MatchupGameViewModel(MatchRow row) => _row = row;

    public string MatchId => _row.MatchId;
    public bool Win => _row.Win;
    public string Result => _row.Win ? "Win" : "Loss";
    public string Kda => $"{_row.Kills}/{_row.Deaths}/{_row.Assists}";
    public string GoldDiff15 => _row.GoldDiffAt15 is int g ? g.ToString("+0;-0;0") : "—";
    public string WhenLocal => DateTimeOffset.FromUnixTimeSeconds(_row.GameStartUtc).LocalDateTime.ToString("MMM d");
}
