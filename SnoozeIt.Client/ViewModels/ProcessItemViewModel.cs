namespace SnoozeIt.Client.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using SnoozeIt.Core.Models;

public partial class ProcessItemViewModel : ObservableObject
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string FriendlyName { get; init; } = "";

    [ObservableProperty]
    private long _workingSetBytes;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private RecommendationTier _tier;

    [ObservableProperty]
    private SnoozeState _state = SnoozeState.Active;

    [ObservableProperty]
    private bool _isPlayingAudio;

    public string WorkingSetFormatted => $"{(WorkingSetBytes / (1024.0 * 1024.0)):F1} MB";
    public string ScoreDisplay => $"{Score}/100";
    public string StatusText => State switch
    {
        SnoozeState.Snoozed => "💤 Snoozed",
        SnoozeState.Thawing => "⚡ Thawing...",
        _ => IsPlayingAudio ? "🔊 Playing Audio" : "🟢 Running"
    };
}
