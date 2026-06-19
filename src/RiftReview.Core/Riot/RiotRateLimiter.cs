namespace RiftReview.Core.Riot;

public sealed class RiotRateLimiter
{
    private readonly ISystemClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _short = new();
    private readonly Queue<DateTimeOffset> _long = new();
    private DateTimeOffset _retryUntil = DateTimeOffset.MinValue;

    private static readonly TimeSpan ShortWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongWindow = TimeSpan.FromSeconds(120);
    private const int ShortMax = 20;
    private const int LongMax = 100;

    public RiotRateLimiter(ISystemClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _clock = clock;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    public void NotifyRetryAfter(TimeSpan retryAfter)
    {
        var until = _clock.UtcNow + retryAfter;
        if (until > _retryUntil)
            _retryUntil = until;
    }

    public async Task WaitForSlotAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = _clock.UtcNow;

                // Honor Retry-After first
                if (now < _retryUntil)
                {
                    await _delay(_retryUntil - now, ct);
                    continue;
                }

                // Evict expired timestamps from both windows
                Trim(_short, now, ShortWindow);
                Trim(_long, now, LongWindow);

                // Check short window (20 req/s)
                if (_short.Count >= ShortMax)
                {
                    await _delay(FreeIn(_short, now, ShortWindow), ct);
                    continue;
                }

                // Check long window (100 req/2 min)
                if (_long.Count >= LongMax)
                {
                    await _delay(FreeIn(_long, now, LongWindow), ct);
                    continue;
                }

                // Slot available — record the timestamp and return
                _short.Enqueue(now);
                _long.Enqueue(now);
                return;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Remove entries that have slid out of the window.
    // A timestamp at exactly (now - window) is >= window old: it no longer
    // counts against the current window, so we evict it (>=, not >).
    private static void Trim(Queue<DateTimeOffset> q, DateTimeOffset now, TimeSpan w)
    {
        while (q.Count > 0 && now - q.Peek() >= w)
            q.Dequeue();
    }

    // How long until the oldest entry in the window rolls off,
    // freeing up one slot.
    private static TimeSpan FreeIn(Queue<DateTimeOffset> q, DateTimeOffset now, TimeSpan w)
    {
        var wait = (q.Peek() + w) - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1);
    }
}
