namespace SnoozeIt.Client.ViewModels;

using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SnoozeIt.Core.Models;

public partial class ProcessGroupViewModel : ObservableObject
{
    public string GroupName { get; init; } = "";
    public string FriendlyName { get; init; } = "";

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalWorkingSetFormatted))]
    private long _totalWorkingSetBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreDisplay))]
    private int _highestScore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstanceCountFormatted))]
    private int _instanceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private SnoozeState _state = SnoozeState.Active;

    [ObservableProperty]
    private RecommendationTier _tier = RecommendationTier.SafeToSnooze;

    public ObservableCollection<ProcessItemViewModel> Items { get; } = new();

    public string TotalWorkingSetFormatted => $"{(TotalWorkingSetBytes / (1024.0 * 1024.0)):F1} MB";
    public string InstanceCountFormatted => $"({InstanceCount} instance{(InstanceCount > 1 ? "s" : "")})";
    public string ScoreDisplay => $"{HighestScore}/100";
    public string StatusText => State switch
    {
        SnoozeState.Snoozed => "💤 Snoozed",
        SnoozeState.Thawing => "⚡ Thawing...",
        _ => "🟢 Running"
    };

    public void Recalculate()
    {
        InstanceCount = Items.Count;
        TotalWorkingSetBytes = Items.Sum(i => i.WorkingSetBytes);
        HighestScore = Items.Count > 0 ? Items.Max(i => i.Score) : 0;
        Tier = Items.Count > 0 ? Items.OrderByDescending(i => i.Score).First().Tier : RecommendationTier.DoNotSnooze;
        State = Items.All(i => i.State == SnoozeState.Snoozed) ? SnoozeState.Snoozed : SnoozeState.Active;

        OnPropertyChanged(nameof(TotalWorkingSetFormatted));
        OnPropertyChanged(nameof(InstanceCountFormatted));
        OnPropertyChanged(nameof(ScoreDisplay));
        OnPropertyChanged(nameof(StatusText));
    }
}

