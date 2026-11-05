namespace SnoozeIt.Client.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnoozeIt.Client.Services;
using SnoozeIt.Core.Engine;
using SnoozeIt.Core.Models;
using SnoozeIt.Core.Native;

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
        ProcessItemViewModel? foundItem = null;
        foreach (var group in Groups)
        {
            var item = group.Items.FirstOrDefault(p => p.ProcessId == pid);
            if (item != null)
            {
                foundItem = item;
                break;
            }
        }

        if (foundItem != null)
        {
            foundItem.State = SnoozeState.Active;
            MoveItemFromSnoozedGroup(foundItem);
            SnoozedCount = Math.Max(0, SnoozedCount - 1);
            FeedbackMessage = $"Auto-thawed {foundItem.FriendlyName} (PID {pid}) on window focus.";
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

        Groups.Clear();

        var snoozedItems = new List<ProcessItemViewModel>();
        var activeGroupDict = new Dictionary<string, ProcessGroupViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in filtered)
        {
            var proc = sample.Process;
            var rec = sample.Recommendation;
            bool isSafe = SafetyFilter.IsSafeToSuspend(proc.ProcessId, proc.ProcessName, out _);

            var itemVm = new ProcessItemViewModel
            {
                ProcessId = proc.ProcessId,
                ProcessName = proc.ProcessName,
                FriendlyName = proc.FriendlyName ?? proc.ProcessName,
                WorkingSetBytes = proc.WorkingSetBytes,
                CpuPercent = proc.CpuPercent,
                Score = rec.Score,
                Tier = rec.Tier,
                IsPlayingAudio = proc.IsPlayingAudio,
                State = proc.State,
                IsSafeToSnooze = isSafe
            };

            if (proc.State == SnoozeState.Snoozed)
            {
                snoozedItems.Add(itemVm);
            }
            else
            {
                if (!activeGroupDict.TryGetValue(proc.ProcessName, out var groupVm))
                {
                    groupVm = new ProcessGroupViewModel
                    {
                        GroupName = proc.ProcessName,
                        FriendlyName = proc.FriendlyName ?? proc.ProcessName
                    };
                    activeGroupDict[proc.ProcessName] = groupVm;
                }
                groupVm.Items.Add(itemVm);
            }
        }

        // Add Pinned Snoozed Services group at top if any are snoozed
        if (snoozedItems.Count > 0)
        {
            var snoozedGroup = new ProcessGroupViewModel
            {
                GroupName = "Snoozed Services",
                FriendlyName = "💤 Snoozed Services",
                IsSnoozedGroup = true,
                IsExpanded = true
            };
            foreach (var item in snoozedItems)
            {
                snoozedGroup.Items.Add(item);
            }
            snoozedGroup.Recalculate();
            Groups.Add(snoozedGroup);
        }

        // Add remaining groups ordered by peak score
        foreach (var groupVm in activeGroupDict.Values.OrderByDescending(g => g.Items.Count > 0 ? g.Items.Max(i => i.Score) : 0))
        {
            groupVm.Recalculate();
            Groups.Add(groupVm);
        }
    }

    [RelayCommand]
    public async Task SnoozeProcessAsync(ProcessItemViewModel item)
    {
        if (item == null || item.State == SnoozeState.Snoozed || !item.IsSafeToSnooze) return;

        var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
        if (result.Success)
        {
            item.State = SnoozeState.Snoozed;
            FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
            _totalReclaimedBytes += result.Value;
            SavedRamText = $"{(_totalReclaimedBytes / (1024.0 * 1024.0)):F1} MB Reclaimed";
            SnoozedCount++;
            FeedbackMessage = $"Successfully snoozed {item.FriendlyName} (PID {item.ProcessId}). Reclaimed {(result.Value / (1024.0 * 1024.0)):F1} MB.";
            MoveItemToSnoozedGroup(item);
        }
        else
        {
            FeedbackMessage = $"Could not snooze {item.FriendlyName} (PID {item.ProcessId}): {result.Error}";
            FindGroupForItem(item)?.Recalculate();
        }
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
            MoveItemFromSnoozedGroup(item);
        }
        else
        {
            item.State = SnoozeState.Snoozed;
            FeedbackMessage = $"Could not resume {item.FriendlyName} (PID {item.ProcessId}): {result.Error}";
            FindGroupForItem(item)?.Recalculate();
        }
    }

    [RelayCommand]
    public async Task SnoozeGroupAsync(ProcessGroupViewModel group)
    {
        if (group == null || group.IsSnoozedGroup) return;

        int snoozed = 0;
        foreach (var item in group.Items.Where(i => i.IsSafeToSnooze && i.State != SnoozeState.Snoozed).ToList())
        {
            var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
            if (result.Success)
            {
                item.State = SnoozeState.Snoozed;
                FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
                _totalReclaimedBytes += result.Value;
                snoozed++;
                MoveItemToSnoozedGroup(item);
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
                if (group.IsSnoozedGroup)
                {
                    MoveItemFromSnoozedGroup(item);
                }
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
        var candidateGroups = Groups.Where(g => !g.IsSnoozedGroup).ToList();
        foreach (var group in candidateGroups)
        {
            foreach (var item in group.Items.Where(p => p.Tier == RecommendationTier.SafeToSnooze && p.IsSafeToSnooze && p.State != SnoozeState.Snoozed).ToList())
            {
                var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
                if (result.Success)
                {
                    item.State = SnoozeState.Snoozed;
                    FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
                    _totalReclaimedBytes += result.Value;
                    count++;
                    MoveItemToSnoozedGroup(item);
                }
            }
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

    private ProcessGroupViewModel GetOrCreateSnoozedGroup()
    {
        var group = Groups.FirstOrDefault(g => g.IsSnoozedGroup);
        if (group == null)
        {
            group = new ProcessGroupViewModel
            {
                GroupName = "Snoozed Services",
                FriendlyName = "💤 Snoozed Services",
                IsSnoozedGroup = true,
                IsExpanded = true
            };
            Groups.Insert(0, group);
        }
        return group;
    }

    private ProcessGroupViewModel GetOrCreateGroupForProcess(string processName, string? friendlyName = null)
    {
        var group = Groups.FirstOrDefault(g => !g.IsSnoozedGroup && g.GroupName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            group = new ProcessGroupViewModel
            {
                GroupName = processName,
                FriendlyName = friendlyName ?? processName,
                IsExpanded = false
            };
            // Insert after snoozed group if present, otherwise at index 0
            int insertIndex = Groups.Any(g => g.IsSnoozedGroup) ? 1 : 0;
            if (insertIndex <= Groups.Count)
            {
                Groups.Insert(insertIndex, group);
            }
            else
            {
                Groups.Add(group);
            }
        }
        return group;
    }

    private void MoveItemToSnoozedGroup(ProcessItemViewModel item)
    {
        var currentGroup = FindGroupForItem(item);
        if (currentGroup != null && !currentGroup.IsSnoozedGroup)
        {
            currentGroup.Items.Remove(item);
            currentGroup.Recalculate();
            if (currentGroup.Items.Count == 0)
            {
                Groups.Remove(currentGroup);
            }
        }

        var snoozedGroup = GetOrCreateSnoozedGroup();
        if (!snoozedGroup.Items.Contains(item))
        {
            snoozedGroup.Items.Add(item);
        }
        snoozedGroup.Recalculate();
    }

    private void MoveItemFromSnoozedGroup(ProcessItemViewModel item)
    {
        var snoozedGroup = Groups.FirstOrDefault(g => g.IsSnoozedGroup);
        if (snoozedGroup != null && snoozedGroup.Items.Contains(item))
        {
            snoozedGroup.Items.Remove(item);
            snoozedGroup.Recalculate();
            if (snoozedGroup.Items.Count == 0)
            {
                Groups.Remove(snoozedGroup);
            }
        }

        var targetGroup = GetOrCreateGroupForProcess(item.ProcessName, item.FriendlyName);
        if (!targetGroup.Items.Contains(item))
        {
            targetGroup.Items.Add(item);
        }
        targetGroup.Recalculate();
    }

    private ProcessGroupViewModel? FindGroupForItem(ProcessItemViewModel item)
    {
        return Groups.FirstOrDefault(g => g.Items.Contains(item));
    }
}
