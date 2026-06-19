using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Configuration;

namespace RiftReview.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;

    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        _matchDepth = store.MatchDepth;
        _defaultRankedOnly = store.DefaultRankedOnly;
    }

    [ObservableProperty] private int _matchDepth;
    [ObservableProperty] private bool _defaultRankedOnly;

    public int MinDepth => SettingsStore.MinDepth;
    public int MaxDepth => SettingsStore.MaxDepth;

    partial void OnMatchDepthChanged(int value)
    {
        _store.MatchDepth = value;               // clamps + persists
        if (_store.MatchDepth != value) { _matchDepth = _store.MatchDepth; OnPropertyChanged(nameof(MatchDepth)); }
    }

    partial void OnDefaultRankedOnlyChanged(bool value) => _store.DefaultRankedOnly = value;
}
