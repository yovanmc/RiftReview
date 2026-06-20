using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// One enemy-laner matchup for a selected champ: aggregates + the games behind it.
public sealed record MatchupRow(
    int OpponentChampionId,
    int Games,
    int Wins,
    double WinRate,
    double? AvgGoldDiff15,
    double? AvgCs10,
    double AvgDeaths,
    double AvgKda,
    double? AvgPre15,
    IReadOnlyList<MatchRow> GamesList);
