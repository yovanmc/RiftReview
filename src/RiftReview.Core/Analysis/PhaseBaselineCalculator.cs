namespace RiftReview.Core.Analysis;

/// Averages per-phase metrics across the player's prior same-role games, mirroring
/// BaselineCalculator. Each metric is null unless >= minGames games supplied a value for it.
public static class PhaseBaselineCalculator
{
    private static readonly string[] Labels = { "Early", "Mid", "Late" };

    public static IReadOnlyList<PhaseBaseline> Average(
        IEnumerable<IReadOnlyList<PhaseStat>> priorGames, int minGames = 3)
    {
        var games = priorGames.ToList();
        var result = new List<PhaseBaseline>();
        foreach (var label in Labels)
        {
            var stats = games
                .Select(g => g.FirstOrDefault(p => p.Label == label))
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            result.Add(new PhaseBaseline(
                label,
                Avg(stats.Select(s => (double?)s.GoldDiffDelta), minGames),
                Avg(stats.Select(s => (double?)s.CsPerMinute), minGames),
                Avg(stats.Select(s => (double?)s.Deaths), minGames),
                Avg(stats.Select(s => s.KillParticipation), minGames)));
        }
        return result;
    }

    private static double? Avg(IEnumerable<double?> values, int minGames)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count < minGames ? null : present.Average();
    }
}
