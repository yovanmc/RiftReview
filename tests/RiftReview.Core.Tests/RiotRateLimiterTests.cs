using RiftReview.Core.Riot;
using Xunit;

public class RiotRateLimiterTests
{
    [Fact]
    public async Task Allows_first_20_without_delay_then_delays_21st()
    {
        var clock = new FakeClock();
        var totalDelay = TimeSpan.Zero;
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) =>
        {
            totalDelay += d;
            clock.Advance(d);
            return Task.CompletedTask;
        };
        var rl = new RiotRateLimiter(clock, delay);

        for (int i = 0; i < 20; i++) await rl.WaitForSlotAsync();
        Assert.Equal(TimeSpan.Zero, totalDelay);
        await rl.WaitForSlotAsync(); // 21st within the same second must wait
        Assert.True(totalDelay > TimeSpan.Zero);
    }

    [Fact]
    public async Task Honors_retry_after()
    {
        var clock = new FakeClock();
        var totalDelay = TimeSpan.Zero;
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) =>
        {
            totalDelay += d;
            clock.Advance(d);
            return Task.CompletedTask;
        };
        var rl = new RiotRateLimiter(clock, delay);
        rl.NotifyRetryAfter(TimeSpan.FromSeconds(5));
        await rl.WaitForSlotAsync();
        Assert.True(totalDelay >= TimeSpan.FromSeconds(5));
    }
}
