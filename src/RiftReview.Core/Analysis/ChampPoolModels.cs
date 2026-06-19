namespace RiftReview.Core.Analysis;

public sealed record ChampStat(
    int ChampionId, int Games, int Wins, int Losses, double WinRate,
    double Kda, double? AvgCs10, double AvgDeaths, IReadOnlyList<int?> TrendCs10,
    string DominantRole);

public sealed record ChampPool(
    IReadOnlyList<ChampStat> All, IReadOnlyList<int> PracticingChampionIds);
