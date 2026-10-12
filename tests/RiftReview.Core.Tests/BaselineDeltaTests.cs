using RiftReview.Core.Analysis;
using Xunit;

public class BaselineDeltaTests
{
    // Above baseline is good when higher is better and bad when lower is better.
    // The percent unit scales the fraction to whole percent and keeps the sign.
    [Theory]
    [InlineData(6.8, 6.0, +1, "", "+0.8", true, false)]
    [InlineData(6.0, 4.0, -1, "", "+2.0", false, true)]
    [InlineData(0.55, 0.50, +1, "%", "+5%", true, false)]
    public void Compute_formats_the_delta_and_judges_it_by_direction(
        double current, double baseline, int dir, string unit, string text, bool isGood, bool isBad)
    {
        var d = BaselineDelta.Compute(current, baseline, dir, unit);
        Assert.Equal(text, d.Text);
        Assert.Equal(isGood, d.IsGood);
        Assert.Equal(isBad, d.IsBad);
    }
}
