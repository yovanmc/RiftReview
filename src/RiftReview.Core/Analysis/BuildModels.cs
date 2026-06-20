namespace RiftReview.Core.Analysis;

/// <summary>One game's completed-item build for a champ in a role (items already filtered + deduped).</summary>
public sealed record MatchBuild(int ChampionId, string Role, bool Win, IReadOnlyList<int> CompletedItems);

/// <summary>Per-item aggregate across a champ's games. WinRate is always paired with Games (sample size).</summary>
public sealed record BuildItemStat(int ItemId, int Games, int Wins, double WinRate);

/// <summary>A champ's best build = top completed items by frequency, each with its own WR + n.</summary>
public sealed record ChampionBestBuild(
    int ChampionId, string Role, int TotalGames, IReadOnlyList<BuildItemStat> Items);
