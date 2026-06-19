using RiftReview.Core.Data;

namespace RiftReview.Core.Configuration;

public sealed class SettingsStore
{
    public const int MinDepth = 20, MaxDepth = 300, DefaultDepth = 150;
    private readonly RiftReviewDb _db;
    public SettingsStore(RiftReviewDb db) => _db = db;

    public int MatchDepth
    {
        get => int.TryParse(_db.GetMeta("settings.match_depth"), out var v) ? v : DefaultDepth;
        set => _db.SetMeta("settings.match_depth", Math.Clamp(value, MinDepth, MaxDepth).ToString());
    }

    public bool DefaultRankedOnly
    {
        get => _db.GetMeta("settings.ranked_only") is not "false";   // default true
        set => _db.SetMeta("settings.ranked_only", value ? "true" : "false");
    }

    public const int MinTrendWindow = 5, MaxTrendWindow = 30, DefaultTrendWindow = 10;

    public int TrendWindow
    {
        get => int.TryParse(_db.GetMeta("settings.trend_window"), out var v) ? Math.Clamp(v, MinTrendWindow, MaxTrendWindow) : DefaultTrendWindow;
        set => _db.SetMeta("settings.trend_window", Math.Clamp(value, MinTrendWindow, MaxTrendWindow).ToString());
    }
}
