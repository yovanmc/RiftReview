using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class SessionCalculatorTests
{
    // helper: a game at unix-second `start`, fixed 1800s duration, given win + deaths + cs10.
    private static MatchRow G(long start, bool win, int deaths = 3, int? cs10 = 70) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 7, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    private const long H = 3600;

    [Fact]
    public void Groups_games_into_sessions_by_time_gap()
    {
        var games = new List<MatchRow>
        {
            G(0, true), G(2400, true), G(4800, false),
            G(4800 + 5*H, false), G(4800 + 5*H + 2400, false),
        };
        var sessions = SessionCalculator.BuildSessions(games);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, sessions[0].Games);
        Assert.Equal(3, sessions[1].Games);
    }

    [Fact]
    public void Detects_trailing_loss_streak_and_marks_tilted()
    {
        var games = new List<MatchRow>
        {
            G(0, true), G(2400, false), G(4800, false), G(7200, false), G(9600, false),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Equal(4, s.EndLossStreak);
        Assert.Equal(4, s.LongestLossStreak);
        Assert.Equal(TiltSeverity.Tilted, s.Severity);
        Assert.Contains(s.Reasons, r => r.Contains("skid"));
    }

    [Fact]
    public void Detects_in_session_deaths_decay()
    {
        var games = new List<MatchRow>
        {
            G(0, true, deaths: 1), G(2400, false, deaths: 2),
            G(4800, true, deaths: 7), G(7200, false, deaths: 9),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.True(s.DecayPresent);
        Assert.True(s.DeathsDelta > 0);
        Assert.True(s.Severity >= TiltSeverity.Caution);
        // reason must carry the delta number (e.g. "deaths climbing (+6.0/game)")
        Assert.Contains(s.Reasons, r => r.Contains("deaths climbing (+"));
    }

    [Fact]
    public void Deaths_decay_reason_includes_numeric_delta()
    {
        // first half avg deaths = 1.0, second half avg deaths = 9.0 → delta = 8.0 ≥ threshold 1.5
        var games = new List<MatchRow>
        {
            G(0, true, deaths: 1), G(2400, true, deaths: 1),
            G(4800, false, deaths: 9), G(7200, false, deaths: 9),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.True(s.DeathsDelta >= SessionCalculator.DeathsDecayThreshold);
        // reason must contain the positive delta in "+X.X/game" form
        var reason = s.Reasons.FirstOrDefault(r => r.StartsWith("deaths climbing"));
        Assert.NotNull(reason);
        Assert.Contains("+", reason);
        Assert.Contains("/game", reason);
    }

    [Fact]
    public void Cs10_decay_reason_includes_numeric_delta()
    {
        // first half cs@10 avg = 100, second half cs@10 avg = 60 → delta = 40 ≥ threshold 8
        var games = new List<MatchRow>
        {
            G(0, true, cs10: 100), G(2400, true, cs10: 100),
            G(4800, false, cs10: 60), G(7200, false, cs10: 60),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.True(s.Cs10Delta >= SessionCalculator.Cs10DecayThreshold);
        var reason = s.Reasons.FirstOrDefault(r => r.StartsWith("CS@10 falling"));
        Assert.NotNull(reason);
        Assert.Contains("(-", reason);
        // must NOT just say "CS@10 falling" with nothing after
        Assert.True(reason!.Length > "CS@10 falling".Length);
    }

    [Fact]
    public void Kda_decay_reason_includes_numeric_delta()
    {
        // first half: kda = (5+7)/max(1,1)=12 each → avg 12; second half: kda=(1+1)/max(1,9)≈0.22 each
        var games = new List<MatchRow>
        {
            G(0, true, deaths: 1), G(2400, true, deaths: 1),
            G(4800, false, deaths: 9), G(7200, false, deaths: 9),
        };
        // Only KDA decay needed — reuse the existing deaths+KDA scenario
        // With deaths=1 first half: KDA=(5+7)/1=12; deaths=9 second half: KDA=(5+7)/9≈1.33 → delta≈10.67
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.True(s.KdaDelta >= SessionCalculator.KdaDecayThreshold);
        var reason = s.Reasons.FirstOrDefault(r => r.StartsWith("KDA falling"));
        Assert.NotNull(reason);
        Assert.Contains("(-", reason);
        Assert.True(reason!.Length > "KDA falling".Length);
    }

    [Fact]
    public void Null_cs10_does_not_crash_and_cs_delta_is_null()
    {
        var games = new List<MatchRow>
        {
            G(0, true, cs10: null), G(2400, true, cs10: null), G(4800, true, cs10: null),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Null(s.Cs10Delta);
    }

    [Fact]
    public void All_wins_is_calm()
    {
        var games = new List<MatchRow> { G(0, true), G(2400, true), G(4800, true) };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Equal(TiltSeverity.Calm, s.Severity);
    }

    [Fact]
    public void Empty_input_is_empty()
    {
        Assert.Empty(SessionCalculator.BuildSessions(new List<MatchRow>()));
    }
}
