using System.Collections.Generic;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class ChampCardViewModel
{
    private readonly ChampStat _s;
    public ChampCardViewModel(ChampStat s, string name) { _s = s; ChampionName = name; }
    public int ChampionId => _s.ChampionId;
    public string DominantRole => _s.DominantRole;
    public string ChampionName { get; }
    public string WinRate => $"{_s.WinRate * 100:0}%";
    public bool Winning => _s.WinRate >= 0.5;
    public string Subtitle => $"{_s.Games} games · {RoleLabel}";
    private string RoleLabel => _s.DominantRole switch
    {
        "TOP" => "top",
        "JUNGLE" => "jungle",
        "MIDDLE" => "mid",
        "BOTTOM" => "bot",
        "UTILITY" => "support",
        "" => "—",
        var other => other.ToLowerInvariant(),
    };
    public string Kda => $"KDA {_s.Kda:0.0}";
    public string Cs10 => _s.AvgCs10 is null ? "CS@10 —" : $"CS@10 {_s.AvgCs10.Value:0}";
    public string Deaths => $"Dth {_s.AvgDeaths:0.0}";
    public IReadOnlyList<int?> Trend => _s.TrendCs10;

    /// <summary>Set after construction by ChampPoolViewModel's async build-loading pass.</summary>
    public BestBuildViewModel? BestBuild { get; set; }
}
