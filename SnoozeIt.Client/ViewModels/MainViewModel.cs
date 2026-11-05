namespace SnoozeIt.Client.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnoozeIt.Client.Services;
using SnoozeIt.Core.Engine;
using SnoozeIt.Core.Models;

public partial class MainViewModel : ObservableObject
{
    private readonly TelemetrySampler _sampler = new();
    private readonly ProcessServiceClient _serviceClient = new();
    public WindowFocusWatcher FocusWatcher { get; }

    [ObservableProperty]
    private string _savedRamText = "0.0 MB Reclaimed";

    [ObservableProperty]
    private int _snoozedCount = 0;

    [ObservableProperty]
    private string _searchFilter = "";

    [ObservableProperty]
    private bool _isServiceConnected = false;

    [ObservableProperty]
    private string _feedbackMessage = "";

    private long _totalReclaimedBytes = 0;

    public ObservableCollection<ProcessGroupViewModel> Groups { get; } = new();

    public MainViewModel()
    {
        FocusWatcher = new WindowFocusWatcher(_serviceClient);
        FocusWatcher.ProcessAutoThawed += OnProcessAutoThawed;
        FocusWatcher.Start();

        _ = RefreshDataAsync();
    }

    private void OnProcessAutoThawed(int pid)
    {
        foreach (var group in Groups)
        {
            var item = group.Items.FirstOrDefault(p => p.ProcessId == pid);
            if (item != null)
            {
                item.State = SnoozeState.Active;
                group.Recalculate();
                SnoozedCount = Math.Max(0, SnoozedCount - 1);
                FeedbackMessage = $"Auto-thawed {item.FriendlyName} (PID {pid}) on window focus.";
                break;
            }
        }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsServiceConnected = await _serviceClient.IsServiceAvailableAsync();
        var samples = _sampler.SampleAllProcesses();

        var filtered = string.IsNullOrWhiteSpace(SearchFilter)
            ? samples
            : samples.Where(s => s.Process.ProcessName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                 (s.Process.FriendlyName ?? "").Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));

        // Group by ProcessName
        var groupedSamples = filtered
            .GroupBy(s => s.Process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(s => s.Recommendation.Score))
            .ToList();

        Groups.Clear();

        foreach (var group in groupedSamples)
        {
            var firstItem = group.First();
            var groupVm = new ProcessGroupViewModel
            {
                GroupName = group.Key,
                FriendlyName = firstItem.Process.FriendlyName ?? group.Key
            };

            foreach (var (proc, rec) in group.OrderByDescending(s => s.Recommendation.Score))
            {
                groupVm.Items.Add(new ProcessItemViewModel
                {
                    ProcessId = proc.ProcessId,
                    ProcessName = proc.ProcessName,
                    FriendlyName = proc.FriendlyName ?? proc.ProcessName,
                    WorkingSetBytes = proc.WorkingSetBytes,
                    CpuPercent = proc.CpuPercent,
                    Score = rec.Score,
                    Tier = rec.Tier,
                    IsPlayingAudio = proc.IsPlayingAudio,
                    State = proc.State
                });
            }

            groupVm.Recalculate();
            Groups.Add(groupVm);
        }
    }

    [RelayCommand]
    public async Task SnoozeProcessAsync(ProcessItemViewModel item)
    {
        if (item == null || item.State == SnoozeState.Snoozed) return;

        var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
        if (result.Success)
        {
            item.State = SnoozeState.Snoozed;
            FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
            _totalReclaimedBytes += result.Value;
            SavedRamText = $"{(_totalReclaimedBytes / (1024.0 * 1024.0)):F1} MB Reclaimed";
            SnoozedCount++;
            FeedbackMessage = $"Successfully snoozed {item.FriendlyName} (PID {item.ProcessId}). Reclaimed {(result.Value / (1024.0 * 1024.0)):F1} MB.";
        }
        else
        {
            FeedbackMessage = $"Could not snooze {item.FriendlyName} (PID {item.ProcessId}): {result.Error}";
        }

        FindGroupForItem(item)?.Recalculate();
    }

    [RelayCommand]
    public async Task ResumeProcessAsync(ProcessItemViewModel item)
    {
        if (item == null || item.State != SnoozeState.Snoozed) return;

        item.State = SnoozeState.Thawing;
        var result = await _serviceClient.ResumeProcessAsync(item.ProcessId);
        if (result.Success)
        {
            item.State = SnoozeState.Active;
            FocusWatcher.UnregisterSnoozedProcess(item.ProcessId);
            SnoozedCount = Math.Max(0, SnoozedCount - 1);
            FeedbackMessage = $"Successfully resumed {item.FriendlyName} (PID {item.ProcessId}).";
        }
        else
        {
            item.State = SnoozeState.Snoozed;
            FeedbackMessage = $"Could not resume {item.FriendlyName} (PID {item.ProcessId}): {result.Error}";
        }

        FindGroupForItem(item)?.Recalculate();
    }

    [RelayCommand]
    public async Task SnoozeGroupAsync(ProcessGroupViewModel group)
    {
        if (group == null) return;

        int snoozed = 0;
        foreach (var item in group.Items.Where(i => i.State != SnoozeState.Snoozed).ToList())
        {
            var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
            if (result.Success)
            {
                item.State = SnoozeState.Snoozed;
                FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
                _totalReclaimedBytes += result.Value;
                snoozed++;
            }
        }

        if (snoozed > 0)
        {
            SavedRamText = $"{(_totalReclaimedBytes / (1024.0 * 1024.0)):F1} MB Reclaimed";
            SnoozedCount += snoozed;
            FeedbackMessage = $"Snoozed {snoozed} instances of {group.FriendlyName}.";
        }
        group.Recalculate();
    }

    [RelayCommand]
    public async Task ResumeGroupAsync(ProcessGroupViewModel group)
    {
        if (group == null) return;

        int resumed = 0;
        foreach (var item in group.Items.Where(i => i.State == SnoozeState.Snoozed).ToList())
        {
            var result = await _serviceClient.ResumeProcessAsync(item.ProcessId);
            if (result.Success)
            {
                item.State = SnoozeState.Active;
                FocusWatcher.UnregisterSnoozedProcess(item.ProcessId);
                resumed++;
            }
        }

        if (resumed > 0)
        {
            SnoozedCount = Math.Max(0, SnoozedCount - resumed);
            FeedbackMessage = $"Resumed {resumed} instances of {group.FriendlyName}.";
        }
        group.Recalculate();
    }

    [RelayCommand]
    public async Task SnoozeAllRecommendedAsync()
    {
        int count = 0;
        foreach (var group in Groups)
        {
            foreach (var item in group.Items.Where(p => p.Tier == RecommendationTier.SafeToSnooze && p.State != SnoozeState.Snoozed).ToList())
            {
                var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
                if (result.Success)
                {
                    item.State = SnoozeState.Snoozed;
                    FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
                    _totalReclaimedBytes += result.Value;
                    count++;
                }
            }
            group.Recalculate();
        }

        if (count > 0)
        {
            SavedRamText = $"{(_totalReclaimedBytes / (1024.0 * 1024.0)):F1} MB Reclaimed";
            SnoozedCount += count;
            FeedbackMessage = $"Snoozed {count} safe background workloads.";
        }
        else
        {
            FeedbackMessage = "No idle workloads eligible for safe hibernation at this moment.";
        }
    }

    private ProcessGroupViewModel? FindGroupForItem(ProcessItemViewModel item)
    {
        return Groups.FirstOrDefault(g => g.Items.Contains(item));
    }
}
