using System.Collections.Generic;
using RiftReview.App.ViewModels;
using RiftReview.Core.Analysis;
using Xunit;

public class MetricTrendViewModelTests
{
    [Fact]
    public void Baseline_fields_round_trip()
    {
        var trend = new MetricTrend(
            "cs10", "CS @ 10", "", +1,
            6.8, 6.0, 0.8,
            TrendVerdict.Improving, new double?[] { 6.0, 6.4, 6.8 });
        var vm = new MetricTrendViewModel(trend)
        {
            HasBaseline     = true,
            BaselineValue   = 6.0,
            BaselineLabel   = "Gold avg",
            DeltaVsBaseline = "+0.8",
            BaselineIsGood  = true,
        };

        Assert.True(vm.HasBaseline);
        Assert.Equal(6.0, vm.BaselineValue);
        Assert.Equal("Gold avg", vm.BaselineLabel);
        Assert.Equal("+0.8", vm.DeltaVsBaseline);
        Assert.True(vm.BaselineIsGood);
        Assert.False(vm.BaselineIsBad);
    }
}
