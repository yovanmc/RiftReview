namespace RiftReview.App.ViewModels;

public sealed class BuildItemRow
{
    public string ItemName { get; init; } = "";
    public int Games { get; init; }
    public double WinRate { get; init; }
    // "7 games · 71% WR" — number ALWAYS shown beside the item (number-under-every-verdict discipline)
    public string Caption => $"{Games} game{(Games == 1 ? "" : "s")} · {WinRate:P0} WR";
}

public sealed class BestBuildViewModel
{
    public int ChampionId { get; init; }
    public string RoleLabel { get; init; } = "";
    public int TotalGames { get; init; }
    public IReadOnlyList<BuildItemRow> Items { get; init; } = Array.Empty<BuildItemRow>();

    public bool HasBuild => Items.Count > 0;
    // honest empty states:
    public bool NotEnoughGames { get; init; }     // <3 games
    public bool ItemDataUnavailable { get; init; } // offline, no item.json cache
    public string EmptyMessage =>
        ItemDataUnavailable ? "Item data unavailable — connect once to load item names."
        : NotEnoughGames    ? "Not enough games yet to show a build."
        : "No completed items recorded yet.";
    public string Summary => $"Best build · {RoleLabel} · {TotalGames} game{(TotalGames == 1 ? "" : "s")}";
}
