using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class ChampRowViewModel
{
    private readonly ChampStat _s;
    public ChampRowViewModel(ChampStat s, string name) { _s = s; ChampionName = name; }
    public int ChampionId => _s.ChampionId;
    public string ChampionName { get; }
    public int Games => _s.Games;
    public string WinRate => $"{_s.WinRate * 100:0}%";
    public string Record => $"{_s.Wins}W {_s.Losses}L";
    public bool Winning => _s.WinRate >= 0.5;
    public string Kda => _s.Kda.ToString("0.0");
    public string Cs10 => _s.AvgCs10 is null ? "—" : _s.AvgCs10.Value.ToString("0");
}
