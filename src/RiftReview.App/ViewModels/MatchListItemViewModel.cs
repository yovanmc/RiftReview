using RiftReview.Core.Data;

namespace RiftReview.App.ViewModels;

public sealed class MatchListItemViewModel
{
    public MatchRow Row { get; }
    public string ChampionName { get; }

    public MatchListItemViewModel(MatchRow row, string championName)
    {
        Row = row;
        ChampionName = championName;
    }

    public string MatchId => Row.MatchId;
    public bool Win => Row.Win;
    public string Result => Row.Win ? "Win" : "Loss";
    public string Role => Row.MyTeamPosition;
    public string Kda => $"{Row.Kills}/{Row.Deaths}/{Row.Assists}";
    public string WhenLocal => DateTimeOffset.FromUnixTimeSeconds(Row.GameStartUtc).LocalDateTime.ToString("MMM d, HH:mm");
}
