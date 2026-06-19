using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using Xunit;

public class SettingsViewModelTests
{
    [Fact]
    public void Editing_persists_to_store()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var store = new SettingsStore(db);
        var vm = new SettingsViewModel(store);
        Assert.Equal(150, vm.MatchDepth);

        vm.MatchDepth = 250;
        vm.DefaultRankedOnly = false;

        Assert.Equal(250, new SettingsStore(db).MatchDepth);
        Assert.False(new SettingsStore(db).DefaultRankedOnly);
    }
}
