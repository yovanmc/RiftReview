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

    [Fact]
    public async Task Long_window_boundary_waits_past_bare_window_for_safety_margin()
    {
        var clock = new FakeClock();
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) => { clock.Advance(d); return Task.CompletedTask; };
        var rl = new RiotRateLimiter(clock, delay);

        var t0 = clock.UtcNow;
        for (int i = 0; i < 100; i++) await rl.WaitForSlotAsync();   // fill the long window
        await rl.WaitForSlotAsync();                                 // 101st must wait out window + margin
        var elapsed = clock.UtcNow - t0;

        // Bare 120s window would grant the 101st at exactly t0+120s. With the 3s safety margin it must
        // be strictly later — assert >= 123s (proves the margin is applied, not just the bare window).
        Assert.True(elapsed >= TimeSpan.FromSeconds(123),
            $"101st call should be granted >= 123s after the first (120s window + 3s margin); got {elapsed.TotalSeconds:0.###}s");
    }
}
