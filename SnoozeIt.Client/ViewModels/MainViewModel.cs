namespace SnoozeIt.Client.ViewModels;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
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
    private string _totalRamText = "0.0 GB";

    [ObservableProperty]
    private string _usedRamText = "0.0 GB";

    [ObservableProperty]
    private string _reclaimedRamText = "0.0 MB";

    [ObservableProperty]
    private string _savedRamText = "0.0 MB Reclaimed";

    [ObservableProperty]
    private int _snoozedCount = 0;

    [ObservableProperty]
    private string _snoozedCountFormatted = "0 Apps";

    [ObservableProperty]
    private string _searchFilter = "";

    [ObservableProperty]
    private bool _isServiceConnected = false;

    [ObservableProperty]
    private string _feedbackMessage = "";

    private readonly ConcurrentDictionary<int, long> _reclaimedBytesByPid = new();
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherQueueTimer? _dispatcherTimer;
    private readonly System.Threading.Timer? _fallbackTimer;

    public ObservableCollection<ProcessGroupViewModel> Groups { get; } = new();

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        FocusWatcher = new WindowFocusWatcher(_serviceClient);
        FocusWatcher.ProcessAutoThawed += OnProcessAutoThawed;
        FocusWatcher.Start();

        UpdateSystemRamMetrics();
        UpdateReclaimedRamDisplay();

        if (_dispatcherQueue != null)
        {
            _dispatcherTimer = _dispatcherQueue.CreateTimer();
            _dispatcherTimer.Interval = TimeSpan.FromSeconds(30);
            _dispatcherTimer.Tick += (s, e) => OnAutoRefreshTimerTick();
            _dispatcherTimer.Start();
        }
        else
        {
            _fallbackTimer = new System.Threading.Timer(_ => OnAutoRefreshTimerTick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        _ = RefreshDataAsync();
    }

    private void OnAutoRefreshTimerTick()
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                UpdateSystemRamMetrics();
                _ = RefreshDataAsync();
            });
        }
        else
        {
            UpdateSystemRamMetrics();
            _ = RefreshDataAsync();
        }
    }

    public void UpdateSystemRamMetrics()
    {
        try
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
            };

            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                ulong totalPhys = memStatus.ullTotalPhys;
                ulong availPhys = memStatus.ullAvailPhys;
                ulong usedPhys = totalPhys > availPhys ? totalPhys - availPhys : 0;

                TotalRamText = $"{(totalPhys / (1024.0 * 1024.0 * 1024.0)):F1} GB";
                UsedRamText = $"{(usedPhys / (1024.0 * 1024.0 * 1024.0)):F1} GB";
            }
        }
        catch
        {
            TotalRamText = "0.0 GB";
            UsedRamText = "0.0 GB";
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
        return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
    }

    private void UpdateReclaimedRamDisplay()
    {
        long totalReclaimed = _reclaimedBytesByPid.Values.Sum();
        string formatted = FormatBytes(totalReclaimed);
        ReclaimedRamText = formatted;
        SavedRamText = $"{formatted} Reclaimed";
        SnoozedCount = _reclaimedBytesByPid.Count;
        SnoozedCountFormatted = $"{SnoozedCount} Apps";
    }

    private void OnProcessAutoThawed(int pid)
    {
        _reclaimedBytesByPid.TryRemove(pid, out _);
        UpdateReclaimedRamDisplay();

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
            FeedbackMessage = $"Auto-thawed {foundItem.FriendlyName} (PID {pid}) on window focus.";
        }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsServiceConnected = await _serviceClient.IsServiceAvailableAsync();
        var samples = _sampler.SampleAllProcesses();

        // Reconcile active vs snoozed processes: if any snoozed process exited or terminated, remove from tracking
        var runningPids = new HashSet<int>(samples.Select(s => s.Process.ProcessId));
        foreach (var snoozedPid in _reclaimedBytesByPid.Keys.ToList())
        {
            if (!runningPids.Contains(snoozedPid))
            {
                _reclaimedBytesByPid.TryRemove(snoozedPid, out _);
                FocusWatcher.UnregisterSnoozedProcess(snoozedPid);
            }
        }
        UpdateReclaimedRamDisplay();

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

            bool isSnoozed = _reclaimedBytesByPid.ContainsKey(proc.ProcessId) || proc.State == SnoozeState.Snoozed;
            var effectiveState = isSnoozed ? SnoozeState.Snoozed : proc.State;

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
                State = effectiveState,
                IsSafeToSnooze = isSafe
            };

            if (isSnoozed)
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
            _reclaimedBytesByPid[item.ProcessId] = result.Value;
            UpdateReclaimedRamDisplay();
            FeedbackMessage = $"Successfully snoozed {item.FriendlyName} (PID {item.ProcessId}). Reclaimed {FormatBytes(result.Value)}.";
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
            _reclaimedBytesByPid.TryRemove(item.ProcessId, out _);
            UpdateReclaimedRamDisplay();
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
                _reclaimedBytesByPid[item.ProcessId] = result.Value;
                snoozed++;
                MoveItemToSnoozedGroup(item);
            }
        }

        if (snoozed > 0)
        {
            UpdateReclaimedRamDisplay();
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
                _reclaimedBytesByPid.TryRemove(item.ProcessId, out _);
                resumed++;
                if (group.IsSnoozedGroup)
                {
                    MoveItemFromSnoozedGroup(item);
                }
            }
        }

        if (resumed > 0)
        {
            UpdateReclaimedRamDisplay();
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
                    _reclaimedBytesByPid[item.ProcessId] = result.Value;
                    count++;
                    MoveItemToSnoozedGroup(item);
                }
            }
        }

        if (count > 0)
        {
            UpdateReclaimedRamDisplay();
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
