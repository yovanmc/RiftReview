using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

// Display wrapper over a PlaySession: labels, severity flags, reasons.
public sealed class SessionRowViewModel
{
    private readonly PlaySession _s;
    public SessionRowViewModel(PlaySession s)
    {
        _s = s;
        Games = s.GamesList.Select(g => new SessionGameViewModel(g)).ToList();
    }

    public string WhenLabel => DateTimeOffset.FromUnixTimeSeconds(_s.StartUtc).LocalDateTime.ToString("MMM d · h:mm tt");
    public string Record => $"{_s.Wins}–{_s.Losses}";
    public string GamesLabel => _s.Games == 1 ? "1 game" : $"{_s.Games} games";
    public TiltSeverity Severity => _s.Severity;
    public bool IsTilted  => _s.Severity == TiltSeverity.Tilted;
    public bool IsCaution => _s.Severity == TiltSeverity.Caution;
    public bool IsCalm    => _s.Severity == TiltSeverity.Calm;
    public string SeverityLabel => _s.Severity switch
    {
        TiltSeverity.Tilted  => "Tilted",
        TiltSeverity.Caution => "Caution",
        _                    => "Calm",
    };
    public string ReasonsText => string.Join(" · ", _s.Reasons);
    public IReadOnlyList<SessionGameViewModel> Games { get; }
}

// Minimal per-game wrapper for the session detail list.
public sealed class SessionGameViewModel
{
    private readonly RiftReview.Core.Data.MatchRow _g;
    public SessionGameViewModel(RiftReview.Core.Data.MatchRow g) => _g = g;
    public bool Win => _g.Win;
    public string Result => _g.Win ? "Win" : "Loss";
    public string Kda => $"{_g.Kills}/{_g.Deaths}/{_g.Assists}";
    public string WhenLocal => DateTimeOffset.FromUnixTimeSeconds(_g.GameStartUtc).LocalDateTime.ToString("h:mm tt");
}
