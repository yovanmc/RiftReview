using RiftReview.Core.Riot;
using Xunit;

public class RiotRoutingTests
{
    [Theory]
    [InlineData("na1", "americas")]
    [InlineData("br1", "americas")]
    [InlineData("euw1", "europe")]
    [InlineData("kr", "asia")]
    [InlineData("jp1", "asia")]
    public void RegionalFor_maps_platform_to_regional(string platform, string expected)
        => Assert.Equal(expected, RiotRouting.RegionalFor(platform));

    [Fact]
    public void RegionalFor_is_case_insensitive()
        => Assert.Equal("americas", RiotRouting.RegionalFor("NA1"));

    [Fact]
    public void RegionalFor_unknown_throws()
        => Assert.Throws<ArgumentException>(() => RiotRouting.RegionalFor("zz9"));
}
