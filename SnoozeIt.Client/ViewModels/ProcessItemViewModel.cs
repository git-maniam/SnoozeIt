namespace SnoozeIt.Client.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using SnoozeIt.Core.Models;

public partial class ProcessItemViewModel : ObservableObject
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string FriendlyName { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkingSetFormatted))]
    private long _workingSetBytes;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreDisplay))]
    private int _score;

    [ObservableProperty]
    private RecommendationTier _tier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private SnoozeState _state = SnoozeState.Active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isPlayingAudio;

    public string PidDisplay => $"PID: {ProcessId}";
    public string WorkingSetFormatted => $"{(WorkingSetBytes / (1024.0 * 1024.0)):F1} MB";
    public string ScoreDisplay => $"{Score}/100";
    public string StatusText => State switch
    {
        SnoozeState.Snoozed => "💤 Snoozed",
        SnoozeState.Thawing => "⚡ Thawing...",
        _ => IsPlayingAudio ? "🔊 Playing Audio" : "🟢 Running"
    };
}
