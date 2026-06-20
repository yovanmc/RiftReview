using System;
using RiftReview.App.Services;
using Xunit;

public class NavigationServiceTests
{
    [Fact]
    public void NavigateTo_raises_event_with_target_type()
    {
        var nav = new NavigationService();
        Type? got = null;
        nav.NavigationRequested += t => got = t;
        nav.NavigateTo(typeof(string));
        Assert.Equal(typeof(string), got);
    }

    [Fact]
    public void NavigateTo_without_subscriber_does_not_throw()
    {
        var nav = new NavigationService();
        nav.NavigateTo(typeof(string));   // no subscriber — must be safe
    }
}
