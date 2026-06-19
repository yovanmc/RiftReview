using RiftReview.Core.Riot;

public sealed class FakeClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan d) => UtcNow += d;
}
