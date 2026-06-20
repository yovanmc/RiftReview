using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

// Display wrapper over a MatchupRow: formats aggregates, maps win% -> thin/favorable/unfavorable flags.
public sealed class MatchupRowViewModel
{
    public const int ConfidenceMinGames = 3;

    private readonly MatchupRow _m;
    public MatchupRowViewModel(MatchupRow m, string opponentName)
    {
        _m = m;
        OpponentName = opponentName;
        GameRows = m.GamesList.Select(g => new MatchupGameViewModel(g)).ToList();
    }

    public string OpponentName { get; }
    public int GamesCount => _m.Games;
    public string GamesLabel => _m.Games + "g";
    public string Record => $"{_m.Wins}–{_m.Games - _m.Wins}";   // e.g. "5–2"
    public string WinPercent => Math.Round(_m.WinRate * 100) + "%";
    public IReadOnlyList<MatchupGameViewModel> GameRows { get; }

    public bool IsThin => _m.Games < ConfidenceMinGames;
    public bool IsFavorable => !IsThin && _m.WinRate >= 0.55;
    public bool IsUnfavorable => !IsThin && _m.WinRate <= 0.45;

    public string GoldDiff15 => Round0(_m.AvgGoldDiff15);                 // "+138" / "-480" / "—"
    public string Cs10 => _m.AvgCs10 is double c ? Math.Round(c).ToString() : "—";
    public string Deaths => Math.Round(_m.AvgDeaths, 1).ToString();
    public string Kda => Math.Round(_m.AvgKda, 1).ToString();
    public string Pre15 => _m.AvgPre15 is double p ? Math.Round(p, 1).ToString() : "—";

    private static string Round0(double? v) => v is double d ? Math.Round(d).ToString("+0;-0;0") : "—";
}
