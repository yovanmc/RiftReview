using RiftReview.Core.Analysis;
using Xunit;

public class BaselineDeltaTests
{
    [Fact]
    public void Higher_is_better_above_baseline_is_good()
    {
        var d = BaselineDelta.Compute(current: 6.8, baseline: 6.0, dir: +1, unit: "");
        Assert.Equal("+0.8", d.Text);
        Assert.True(d.IsGood);
        Assert.False(d.IsBad);
    }

    [Fact]
    public void Lower_is_better_above_baseline_is_bad()
    {
        var d = BaselineDelta.Compute(current: 6.0, baseline: 4.0, dir: -1, unit: "");
        Assert.Equal("+2.0", d.Text);
        Assert.False(d.IsGood);
        Assert.True(d.IsBad);
    }

    [Fact]
    public void Percent_unit_scales_and_signs()
    {
        var d = BaselineDelta.Compute(current: 0.55, baseline: 0.50, dir: +1, unit: "%");
        Assert.Equal("+5%", d.Text);
    }
}
