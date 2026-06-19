using RiftReview.Core.Analysis;

public class BaselineCalculatorTests
{
    [Fact]
    public void Averages_pace_per_minute_across_games()
    {
        var g1 = new List<ChartPoint> { new(1, 4), new(2, 6) };
        var g2 = new List<ChartPoint> { new(1, 6), new(2, 8) };
        var g3 = new List<ChartPoint> { new(1, 5), new(2, 7) };
        var b = BaselineCalculator.Average(new[] { g1, g2, g3 }, minGames: 3);
        Assert.Equal(5, b.Single(p => p.Minute == 1).Value);
        Assert.Equal(7, b.Single(p => p.Minute == 2).Value);
    }

    [Fact]
    public void Returns_empty_when_too_few_games()
        => Assert.Empty(BaselineCalculator.Average(new[] { new List<ChartPoint> { new(1, 4) } }, minGames: 3));
}
